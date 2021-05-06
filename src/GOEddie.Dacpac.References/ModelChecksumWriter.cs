using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml;

namespace GOEddie.Dacpac.References
{
    public class ModelChecksumWriter
    {
        private readonly string _checksumProvider;
        private readonly string _path;

        public ModelChecksumWriter(string path,
            string checksumProvider = "System.Security.Cryptography.SHA256CryptoServiceProvider")
        {
            _path = path;
            _checksumProvider = checksumProvider;
        }

        public void FixChecksum(string filename = "model.xml")
        {
            using (var dac = new DacHacXml(_path))
            {
                var originXml = dac.GetXml("Origin.xml");

                var sourceXml = dac.GetStream(filename);

                var calculatedChecksum =
                    BitConverter.ToString(
                        (HashAlgorithm.Create(_checksumProvider)
                            .ComputeHash(sourceXml))).Replace("-", "");
                ;

                var reader = XmlReader.Create(new StringReader(originXml));
                reader.MoveToContent();


                while (reader.Read())
                {
                    if (reader.Name == "Checksum" && reader.GetAttribute("Uri") == string.Format("/{0}", filename))
                    {
                        var oldChecksum = reader.ReadInnerXml();

                        if (oldChecksum == calculatedChecksum)
                            return;

                        originXml = originXml.Replace(oldChecksum, calculatedChecksum);

                        dac.SetXml("Origin.xml", originXml);


                        return;
                    }
                }
            }
        }
    }
}