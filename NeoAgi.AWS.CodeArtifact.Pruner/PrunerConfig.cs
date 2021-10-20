using NeoAgi.CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner
{
    public class PrunerConfig
    {
        [Option(FriendlyName = "Cache Location", ShortName = "c", LongName = "cacheLocation", Description = "Directory to hold for local cache.", Required = false)]
        public string CacheLocation { get; set; } = string.Empty;
        [Option(FriendlyName = "AWS Account ID", ShortName = "a", LongName = "account", Description = "AWS Account ID to use.  Only necessary if domain and namespace are not unique to the principal provided.", Required = false)]
        public string AccountID { get; set; } = string.Empty;
        [Option(FriendlyName = "Artifact Domain", ShortName = "d", LongName = "domain", Description = "AWS Artifact Domain to query with.", Required = true)]
        public string Domain { get; set; } = string.Empty;
        [Option(FriendlyName = "Artifact Repository", ShortName = "r", LongName = "repository", Description = "AWS Artifact Repository to query with.", Required = true)]
        public string Repository { get; set; } = string.Empty;
    }
}
