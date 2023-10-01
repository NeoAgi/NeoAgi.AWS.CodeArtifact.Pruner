using Amazon.CodeArtifact.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    public class Package
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public List<PackageVersion> Versions { get; set; } = new List<PackageVersion>();

        public Package() { }

        public Package(PackageSummary summary)
        {
            
        }
    }
}
