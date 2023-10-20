using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Policies
{
    public interface IPolicy
    {
        string Identifier { get; }
        public bool IsMatch(Package package);
        public IEnumerable<PackageVersion> Match(Package package);
    }
}
