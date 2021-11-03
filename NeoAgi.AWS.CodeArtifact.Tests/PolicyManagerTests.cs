using System;
using NUnit.Framework;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Tests
{
    public class PolicyManagerTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void OutOfPolicyTest()
        {
            List<Package> packages = new List<Package>();
            packages.Add(new Package()
            {
                Name = "Microsoft.Extensions.Hosting",
                Versions = new List<PackageVersion>()
                {
                    new PackageVersion() { Version = "4.0.0" },
                    new PackageVersion() { Version = "3.0.1" },
                    new PackageVersion() { Version = "4.0.2" },
                    new PackageVersion() { Version = "2.0.3" }
                }
            });

            packages.Add(new Package()
            {
                Name = "NeoAgi",
                Versions = new List<PackageVersion>()
                {
                    new PackageVersion() { Version = "1.0.0" },
                    new PackageVersion() { Version = "1.0.1" },
                    new PackageVersion() { Version = "1.0.2" },
                    new PackageVersion() { Version = "1.0.3" }
                }
            });

            PolicyManager<Package> manager = new PolicyManager<Package>();
            manager.Policies.Add(new PersistVersionCount("NeoAgi*", int.MaxValue));
            manager.Policies.Add(new PersistVersionCount("*", 2));

            Task<IEnumerable<Package>> results = manager.OutOfPolicyAsync(packages);

            List<Package> remaining = new List<Package>(results.Result);
            Package? neoagi = remaining.Find(p => p.Name.Equals("NeoAgi", StringComparison.Ordinal));
            Package? microsoft = remaining.Find(p => p.Name.Equals("Microsoft.Extensions.Hosting", StringComparison.Ordinal));

            Assert.IsTrue(neoagi == null) ;
            Assert.IsTrue(microsoft != null && microsoft.Versions.Count == 2);

            Assert.Pass();
        }
    }
}