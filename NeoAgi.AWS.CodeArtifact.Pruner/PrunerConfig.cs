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
        [Option(FriendlyName = "Logging Level", ShortName = "ll", LongName = "loglevel", Description = "Minimum Logging Level to emit.  Availabile options are None, Trace, Debug, Information, Warning, Error, Critical.  Default is Information.", Required = false)]
        public string LogLevel { get; set; } = "Warning";

        [Option(FriendlyName = "AWS Account ID", ShortName = "a", LongName = "account", Description = "AWS Account ID to use.  Only necessary if domain and namespace are not unique to the principal provided.", Required = false)]
        public string AccountID { get; set; } = string.Empty;

        [Option(FriendlyName = "Artifact Domain", ShortName = "d", LongName = "domain", Description = "AWS Artifact Domain to query with.", Required = true)]
        public string Domain { get; set; } = string.Empty;

        [Option(FriendlyName = "Artifact Repository", ShortName = "r", LongName = "repository", Description = "AWS Artifact Repository to query with.", Required = true)]
        public string Repository { get; set; } = string.Empty;

        [Option(FriendlyName = "Page Limit", ShortName = "p", LongName = "pageLimit", Description = "Maximum number of pages to return from AWS Calls.  Default to 50 pages, or about 2,500 packages.")]
        public int PageLimit { get; set; } = 50;

        [Option(FriendlyName = "Checkpoint Interval", ShortName = "i", LongName = "checkpointInterval", Description = "Number of items processed before a checkpoint information log is emitted.  Set to 0 to disable.")]
        public int CheckpointInterval { get; set; } = 100;

        [Option(FriendlyName = "Dry Run", ShortName = "dr", LongName = "dry-run", Description = "If set to true, all logs will report as if changed occurred yet no modifications will be made.  Default is false.")]
        public bool DryRun { get; set; } = false;

        [Option(FriendlyName = "Parallalism", LongName = "parallelism", Description = "The number of concurrent tasks to process at once.  A higher number will increase TPS.")]
        public int Parallalism { get; set; } = 5;

        [Option(FriendlyName = "Maximum Transactions Per Second", LongName = "tps", Description = "A hard limit of transactions per second to enforce.")]
        public int MaxTransactionsPerSecond { get; set; } = 30;

        [Option(FriendlyName = "Policy JSON", LongName = "policy", Description = "Policy as a JSON String. Structure: [{'Namespace': 'PREFIX.*','VersionsToKeep':INT}]", Required = true)]
        public string Policy { get; set; } = string.Empty;
    }
}
