using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Policies
{
    public class PersistVersionCount : IPolicy
    {
        public string Namespace { get; set; } = string.Empty;
        public int VersionsToKeep { get; set; } = int.MaxValue;

        public PersistVersionCount(string nameSpace, int versionsToKeep)
        {
            Namespace = nameSpace;
            VersionsToKeep = versionsToKeep;
        }

        public bool IsMatch(Package package)
        {
            string effectiveNamespace = Namespace;
            if (Namespace.Contains('*'))
            {
                effectiveNamespace = Namespace.Substring(0, Namespace.IndexOf('*'));
            }

            return package.Name.StartsWith(effectiveNamespace, StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerable<PackageVersion> Match(Package package)
        {
            if(IsMatch(package))
            {
                return Reduce(package);
            }

            return package.Versions;
        }

        protected IEnumerable<PackageVersion> Reduce(Package package)
        {
            // If we're keeping zero versions, bail early
            if(VersionsToKeep <= 0)
                return Enumerable.Empty<PackageVersion>();

            // If we're keeping the upper bounds, just bail early
            if (VersionsToKeep == int.MaxValue)
                return package.Versions;

            // Otherwise, enumerate and return
            List<PackageVersion> retVersions = new List<PackageVersion>(package.Versions.OrderByDescending(x => x.Version).Take(VersionsToKeep));

            return retVersions;
        }
    }
}
