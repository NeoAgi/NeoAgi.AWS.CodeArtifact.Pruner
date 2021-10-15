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
        [Option(FriendlyName = "File Location", ShortName = "l", LongName = "location", Description = "Path of the File to Parse", Required = true)]
        public string FileLocation { get; set; } = string.Empty;
        [Option(FriendlyName = "Category", ShortName = "c", LongName = "category", Description = "Name of the Category", Required = true)]
        public string Category { get; set; } = string.Empty;
    }
}
