using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    internal class QueuedActionDeleteVersion : QueuedAction
    {
        public Package Package { get; set; }
        public IEnumerable<PackageVersion> Versions { get; set; }

        public QueuedActionDeleteVersion(Package package, IEnumerable<PackageVersion> versions)
        {
            Package = package;
            Versions = versions;
        }
    }
}
