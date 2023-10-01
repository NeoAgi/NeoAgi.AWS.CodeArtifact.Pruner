using System;
using NUnit.Framework;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;
using System.Linq;

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
            string domain = string.Empty;
            string repository = string.Empty;

            PolicyManager manager = new PolicyManager();
            manager.Policies.Add(new PersistVersionCount("NeoAgi*", int.MaxValue));
            manager.Policies.Add(new PersistVersionCount("*", 2));

            var neoAgiPackage = new Package(domain, repository)
            {
                Name = "Microsoft.Extensions.Hosting",
                Versions = new List<PackageVersion>()
                {
                    new PackageVersion() { Version = "4.0.0" },
                    new PackageVersion() { Version = "3.0.1" },
                    new PackageVersion() { Version = "4.0.2" },
                    new PackageVersion() { Version = "2.0.3" }
                }
            };
            IEnumerable<PackageVersion> results = manager.VersionsOutOfPolicy(neoAgiPackage);
            Assert.IsTrue(results.Count() == 0, $"Policy executed impropertly for Keep All Case.  Expected 0, received {results.Count()}.");

            var microsoftPackage = new Package(domain, repository)
            {
                Name = "NeoAgi",
                Versions = new List<PackageVersion>()
                {
                    new PackageVersion() { Version = "1.0.0" },
                    new PackageVersion() { Version = "1.0.1" },
                    new PackageVersion() { Version = "1.0.2" },
                    new PackageVersion() { Version = "1.0.3" }
                }
            };

            results = manager.VersionsOutOfPolicy(neoAgiPackage);
            Assert.IsTrue(results.Count() == 2, $"Policy executed impropertly for Keep 2 Case.  Expected 2, received {results.Count()}.");

            Assert.Pass();
        }
    }
}