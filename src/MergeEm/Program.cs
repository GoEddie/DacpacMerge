using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ObjectsComparer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;


namespace MergeEm
{
    using GOEddie.Dacpac.References;

    class Program
    {
        static void Main(string[] args)
        {
            int returnCode = 0; 

            try
            {
                if (args.Length < 3 || args.Any(p => p == "/?") || args.Any(p => p == "-?") || args.Any(p => p == "/help") || args.Any(p => p == "--help"))
                {
                    Console.WriteLine("you need at least three args - targetDacPac (which will be created) sourceDacpac sourceDacpac");
                    returnCode = 1;
                }

                var stopwatch = new Stopwatch();

                stopwatch.Start();

                var target = args.First<string>();
                var sources = args.Skip(1).ToArray();

                var merger = new DacpacMerge(args[0], sources);
                merger.Merge();

                stopwatch.Stop();

                Console.WriteLine("Completed merging {0} dacpacs in {1} seconds.", args.Length - 1, stopwatch.Elapsed.TotalSeconds);
#if DEBUG
                Console.ReadLine();
#endif
                Environment.Exit(returnCode);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);

#if DEBUG
                Console.ReadLine();
#endif
                Environment.Exit(e.HResult);
            }
        }
    }

    class DacpacMerge
    {

        private string[] _sources;
        private TSqlModel _first;
        private string _targetPath;
        private TSqlModel _target;
        private List<CustomData> _globalHeaders = new List<CustomData>();

        public DacpacMerge(string target, params string[] sources)
        {
            _sources = sources;
            _targetPath = target;
        }

        public void Merge()
        {
            var pre = String.Empty;
            var post = String.Empty;

            foreach (var source in _sources)
            {

                if (!File.Exists(source))
                {
                    Console.WriteLine("File {0} does not exist and is being skipped.", source);
                    continue;
                }

                Console.WriteLine("{0} : Processing dacpac {1}", DateTimeOffset.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture), source);


                if (source == _sources.First())
                {
                    Console.WriteLine("{0}: Copying dacpac options from {1} to {2}", DateTimeOffset.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture), source, _targetPath);

                    _first = new TSqlModel(_sources.First());
                    var options = _first.CopyModelOptions();
                    _target = new TSqlModel(_first.Version, options);
                }

                var model = getModel(source);

                foreach (var obj in model.GetObjects(DacQueryScopes.UserDefined))
                {
                    TSqlScript ast;
                    if (obj.TryGetAst(out ast))
                    {
                        var name = obj.Name.ToString();
                        var info = obj.GetSourceInformation();
                        if (info != null && !string.IsNullOrWhiteSpace(info.SourceName))
                        {
                            name = info.SourceName;
                        }

                        if (!string.IsNullOrWhiteSpace(name) && !name.EndsWith(".xsd"))
                        {
                            _target.AddOrUpdateObjects(ast, name, new TSqlObjectOptions());    //WARNING throwing away ansi nulls and quoted identifiers!
                        }
                    }
                }

                AddGlobalCustomData(new HeaderParser(source).GetCustomData()
                    .Where(x => x.Type != "Assembly").ToList());

                using (var package = DacPackage.Load(source))
                {
                    if (!(package.PreDeploymentScript is null))
                    {
                        pre += new StreamReader(package.PreDeploymentScript).ReadToEnd();
                    }
                    if (!(package.PostDeploymentScript is null))
                    {
                        post += new StreamReader(package.PostDeploymentScript).ReadToEnd();
                    }
                }
            }

            WriteFinalDacpac(_target, pre, post);
        }

        private void WriteFinalDacpac(TSqlModel model, string preScript, string postScript)
        {
            var metadata = new PackageMetadata();
            metadata.Name = "dacpac";

            DacPackageExtensions.BuildPackage(_targetPath, model, metadata);

            var writer = new HeaderWriter(_targetPath, new DacHacFactory());
            foreach (var customData in _globalHeaders)
            {
                writer.AddCustomData(customData);
            }
            writer.Close();

            AddScripts(preScript, postScript, _targetPath);
            model.Dispose();
        }

        TSqlModel getModel(string source)
        {
            if (source == _sources.FirstOrDefault<string>())
            {
                return _first;
            }

            try
            {
                return new TSqlModel(source);
            }
            catch (DacModelException e) when (e.Message.Contains("Required references are missing."))
            {
                throw new DacModelException("Failed to load model from DACPAC. "
                    + "A reason might be that the \"SuppressMissingDependenciesErrors\" isn't set to 'true' consistently. ",
                    e);
            }
        }

        private void AddScripts(string pre, string post, string dacpacPath)
        {
            using (var package = Package.Open(dacpacPath, FileMode.Open, FileAccess.ReadWrite))
            {
                if (!string.IsNullOrEmpty(pre))
                {
                    var part = package.CreatePart(new Uri("/predeploy.sql", UriKind.Relative), "text/plain");

                    using (var stream = part.GetStream())
                    {
                        stream.Write(Encoding.UTF8.GetBytes(pre), 0, pre.Length);
                    }
                }

                if (!string.IsNullOrEmpty(post))
                {
                    var part = package.CreatePart(new Uri("/postdeploy.sql", UriKind.Relative), "text/plain");

                    using (var stream = part.GetStream())
                    {
                        stream.Write(Encoding.UTF8.GetBytes(post), 0, post.Length);
                    }
                }
                package.Close();
            }
        }

        private void AddGlobalCustomData(List<CustomData> newCustomData)
        {

            if (_globalHeaders.Count == 0)
            {
                _globalHeaders.AddRange(newCustomData);
                return;
            }

            foreach (var customData in newCustomData)
            {
                var exists = false;

                foreach (var header in _globalHeaders)
                {

                    var isEqual = CustomDataEquals(customData, header);

                    //var comparer = new ObjectsComparer.Comparer<CustomData>();
                    //var isEqual = comparer.Compare(header, customData, out IEnumerable<Difference> differences);

                    if (isEqual)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    _globalHeaders.Add(customData);
                }
            }
        }

        private bool CustomDataEquals(CustomData object1, CustomData object2)
        {
            if (object2.GetType() != object1.GetType()) return false;

            return object1.Category.Equals(object2.Category) 
                   && ItemsEquals(object1.Items, object2.Items);
        }

        private bool ItemsEquals(List<Metadata> object1, List<Metadata> object2)
        {
            if (object2.GetType() != object1.GetType() || object1.Count != object2.Count) return false;

            var isEqual = true;
            for (var itemIndex = 0; itemIndex < object1.Count; itemIndex++)
            {
                if (object1[0].Name != object2[0].Name || object1[0].Value != object2[0].Value)
                {
                    return false;
                }
            }
            return isEqual;
        }

    }
}
