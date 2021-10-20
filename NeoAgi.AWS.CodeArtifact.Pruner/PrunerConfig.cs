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
        [Option(FriendlyName = "Logging Level", ShortName = "ll", LongName = "loglevel", Description = "Minimum Logging Level to emit.  Availabile options are None, Debug, Information, Warning, Error, Critical.  Default is Information.", Required = false)]
        public string LogLevel { get; set; } = "information";
        [Option(FriendlyName = "Cache TTL", ShortName = "ttl", LongName = "cacheTtl", Description = "Number of hours to consider the Cache Location valid for.  Default is 8 hours.  Set to 0 to delete cache.", Required = false)]
        public int CacheTTL { get; set; } = 8;
        [Option(FriendlyName = "AWS Account ID", ShortName = "a", LongName = "account", Description = "AWS Account ID to use.  Only necessary if domain and namespace are not unique to the principal provided.", Required = false)]
        public string AccountID { get; set; } = string.Empty;
        [Option(FriendlyName = "Artifact Domain", ShortName = "d", LongName = "domain", Description = "AWS Artifact Domain to query with.", Required = true)]
        public string Domain { get; set; } = string.Empty;
        [Option(FriendlyName = "Artifact Repository", ShortName = "r", LongName = "repository", Description = "AWS Artifact Repository to query with.", Required = true)]
        public string Repository { get; set; } = string.Empty;
    }
}
