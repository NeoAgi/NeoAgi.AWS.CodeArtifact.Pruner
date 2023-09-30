using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Policies
{
    public class PolicyManager<T> where T : Package, new()
    {
        public List<IPolicy> Policies { get; set; } = new List<IPolicy>();

        public PolicyManager()
        {
            
        }

        public async Task<IEnumerable<T>> OutOfPolicyAsync(List<T> packages)
        {
            return await Task<IEnumerable<T>>.Factory.StartNew(() =>
            {
                List<T> outOfPolicy = new List<T>();

                foreach (Package package in packages)
                {
                    foreach (IPolicy policy in Policies)
                    {
                        if (policy.IsMatch(package))
                        {
                            T pkg = new T
                            {
                                Name = package.Name,
                                Format = package.Format,
                                Namespace = package.Namespace
                            };

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
                                    pkg.Versions.Add(version);
                            }

                            if (pkg.Versions.Count > 0)
                                outOfPolicy.Add(pkg);

                            break;
                        }
                    }
                }

                return outOfPolicy;
            });
        }

    }
}
