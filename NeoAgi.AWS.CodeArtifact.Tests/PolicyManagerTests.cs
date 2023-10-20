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
        private static readonly string DOMAIN = string.Empty;
        private static readonly string REPOSITORY = string.Empty;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void OutOfPolicyTest()
        {
            PolicyManager manager = new PolicyManager(new List<IPolicy>() {
                new PersistVersionCount("NeoAgi*", int.MaxValue),
                new PersistVersionCount("NeoAgi.Example*", 0),
                new PersistVersionCount("*", 2)
            });

            var microsoftPackage = new Package(DOMAIN, REPOSITORY)
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
            IEnumerable<PackageVersion> results = manager.VersionsOutOfPolicy(microsoftPackage);
            Assert.IsTrue(results.Count() == 2, $"Policy executed impropertly for Keep 2 Case.  Expected 2, received {results.Count()}.");

            var neoAgiPackage = new Package(DOMAIN, REPOSITORY)
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
            Assert.IsTrue(results.Count() == 0, $"Policy executed impropertly for Keep All Case.  Expected 0, received {results.Count()}.");

            var neoAgiExamplePackage = new Package(DOMAIN, REPOSITORY)
            {
                Name = "neoagi.example20220915220248",
                Versions = new List<PackageVersion>()
                {
                    new PackageVersion() { Version = "4.0.0" },
                    new PackageVersion() { Version = "3.0.1" },
                    new PackageVersion() { Version = "4.0.2" },
                    new PackageVersion() { Version = "2.0.3" }
                }
            };

            results = manager.VersionsOutOfPolicy(neoAgiExamplePackage);
            Assert.IsTrue(results.Count() == 4, $"Policy executed impropertly for Keep None Case.  Expected 0, received {results.Count()}.");

            Assert.Pass();
        }

        protected static string[] expectedStrings = new string[] {
            "neoagi.example202209152202489",
            "Microsoft.Extensions.Hosting",
            "Microsoft.Extensions",
            "Microsoft",
            "NeoAgi*"
        };

        [Test]
        public void SortPolies()
        {
            PolicyManager manager = new PolicyManager(new List<IPolicy>() {
                new PersistVersionCount(expectedStrings[4], int.MaxValue),
                new PersistVersionCount(expectedStrings[2], 0),
                new PersistVersionCount(expectedStrings[1], int.MaxValue),
                new PersistVersionCount(expectedStrings[3], int.MaxValue),
                new PersistVersionCount(expectedStrings[0], 2)
            });

            var policies = manager.Policies;
            for (int i = 0; i < expectedStrings.Length; i++)
            {
                Assert.IsTrue(policies[i].Identifier.Equals(expectedStrings[i], StringComparison.OrdinalIgnoreCase), $"Sorting of versions was not correct.  Expected '{expectedStrings[i]}' at index {i} received {policies[i].Identifier}");
            }

            Assert.Pass();
        }

        protected static string[] expectedVersions = new string[] { "2.0.3", "3.0.1", "4.0.0", "4.0.2-alpha", "4.0.2" };

        [Test]
        public void SortPackageVersion()
        {
            var package = new Package(DOMAIN, REPOSITORY)
            {
                Name = "neoagi.example20220915220248",
                Versions = new List<PackageVersion>()
                {
                    new PackageVersion() { Version = expectedVersions[2] },
                    new PackageVersion() { Version = expectedVersions[1] },
                    new PackageVersion() { Version = expectedVersions[4] },
                    new PackageVersion() { Version = expectedVersions[3] },
                    new PackageVersion() { Version = expectedVersions[0] }
                }
            };

            var sortedVersions = package.SortVersions();
            for(int i = 0; i < expectedVersions.Length; i++) 
            {
                Assert.IsTrue(sortedVersions[i].Version.Equals(expectedVersions[i]), $"Sorting of versions was not correct.  Expected '{expectedVersions[i]}' at index {i} received {sortedVersions[i].Version}");
            }

            Assert.Pass();
        }
    }
}