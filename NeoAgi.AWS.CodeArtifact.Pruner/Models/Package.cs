using Amazon.CodeArtifact.Model;
using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    public class Package
    {
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public List<PackageVersion> Versions { get; set; } = new List<PackageVersion>();

        public Package(string domain, string repository) 
        {
            Domain = domain;
            Repository = repository;
        }

        public Package(string domain, string repository, PackageSummary summary)
            : this(domain, repository)
        {
            Name = summary.Package;
            Format = summary.Format.Value;
            Namespace = summary.Namespace;
        }

        public List<PackageVersion> SortVersions()
        {
            return Versions.OrderBy(x => SemVersion.Parse(x.Version, SemVersionStyles.Strict)).ToList();
        }
    }
}
