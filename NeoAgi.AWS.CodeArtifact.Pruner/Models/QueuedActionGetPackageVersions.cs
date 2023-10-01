using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    internal class QueuedActionGetPackageVersions : QueuedAction
    {
        public Package Package { get; set; }

        public QueuedActionGetPackageVersions(Package package) 
        {
            Package = package;
        }
    }
}
