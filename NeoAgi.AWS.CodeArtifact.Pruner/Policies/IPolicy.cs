using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Policies
{
    public interface IPolicy
    {
        public bool IsMatch(Package package);
        public IEnumerable<PackageVersion> Match(Package package);
    }
}
