using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Policies
{
    public class PolicyManager
    {
        public List<IPolicy> Policies { get; protected set; }

        public PolicyManager(List<IPolicy> policies)
        {
            Policies = policies;
            SortPolicies();
        }

        public PolicyManager(IEnumerable<IPolicy> policies)
            : this(new List<IPolicy>(policies)) { }

        public IEnumerable<PackageVersion> VersionsOutOfPolicy(Package package)
        {
            List<PackageVersion> versionsOutOfPolicy = new List<PackageVersion>();

            SortPolicies();

            foreach (IPolicy policy in Policies)
            {
                // Do we have a match in this policy?
                if (policy.IsMatch(package))
                {
                    List<PackageVersion> versionsInPolicy = new List<PackageVersion>(policy.Match(package));
                    foreach (PackageVersion version in package.Versions)
                    {
                        bool isFound = false;
                        foreach (PackageVersion inPolicyVersion in versionsInPolicy)
                        {
                            if (inPolicyVersion.Version.Equals(version.Version, StringComparison.OrdinalIgnoreCase) && inPolicyVersion.Name.Equals(version.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                isFound = true;
                                break;
                            }
                        }

                        if (!isFound)
                            versionsOutOfPolicy.Add(version);
                    }

                    break;
                }
            }

            return versionsOutOfPolicy;
        }

        protected void SortPolicies()
        {
            Policies.Sort((x, y) => y.Identifier.Length.CompareTo(x.Identifier.Length));
        }
    }
}
