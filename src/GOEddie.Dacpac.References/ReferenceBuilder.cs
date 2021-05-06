namespace GOEddie.Dacpac.References
{
    public class ReferenceBuilder
    {
        public CustomData BuildThisDatabaseReference(string fileName, string logicalName)
        {
            var customData = GetCustomData();
            customData.AddMetadata("FileName", fileName);
            customData.AddMetadata("LogicalName", logicalName);
            return customData;
        }

        public CustomData BuildSystemDatabaseReference(string dbName, string fileName, string logicalName)
        {
            var customData = GetCustomData();
            customData.AddMetadata("FileName", fileName);
            customData.AddMetadata("LogicalName", logicalName);
            customData.AddMetadata("ExternalParts", string.Format("[{0}]", dbName));
            customData.AddMetadata("SuppressMissingDependenciesErrors", "False");

            return customData;
        }

        public CustomData BuildOtherDatabaseReference(string dbName, string fileName, string logicalName)
        {
            var customData = GetCustomData();
            customData.AddMetadata("FileName", fileName);
            customData.AddMetadata("LogicalName", logicalName);
            customData.AddMetadata("ExternalParts", string.Format("[$({0})]", dbName));
            customData.AddMetadata("SuppressMissingDependenciesErrors", "False");

            customData.RequiredSqlCmdVars.Add(dbName);
            return customData;
        }

        public CustomData BuildOtherServerReference(string dbName, string fileName, string logicalName, string server)
        {
            var customData = GetCustomData();
            customData.AddMetadata("FileName", fileName);
            customData.AddMetadata("LogicalName", logicalName);
            customData.AddMetadata("ExternalParts", string.Format("[$({0})].[$({1})]", server, dbName));
            customData.AddMetadata("SuppressMissingDependenciesErrors", "False");

            customData.RequiredSqlCmdVars.Add(server);
            customData.RequiredSqlCmdVars.Add(dbName);

            return customData;
        }

        private CustomData GetCustomData()
        {
            var customData = new CustomData("Reference", "SqlSchema");

            return customData;
        }
    }
}