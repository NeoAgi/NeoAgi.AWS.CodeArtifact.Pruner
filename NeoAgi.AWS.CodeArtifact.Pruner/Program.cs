// See https://aka.ms/new-console-template for more information

using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NeoAgi.Runtime.Serialization.Json;

var client = new AmazonCodeArtifactClient(new AmazonCodeArtifactConfig()
{
    RegionEndpoint = Amazon.RegionEndpoint.USWest2,
});

string cache = "h:\\package-cache.json";

string domain = "neoagi";
string repository = "neoagi";

var request = new ListPackagesRequest()
{
    Domain = domain,
    Repository = repository
};

List<Package> packages = new List<Package>();

if (File.Exists(cache))
{
    string json = File.ReadAllText(cache);
    packages = json.FromJson<List<Package>>();
}

// Reach out to the API if we have an empty structure
if (packages.Count == 0)
{
    int iterationCount = 0;
    int iterationMax = 50;
    while (iterationCount == 0 || (request.NextToken != null && iterationCount < iterationMax))
    {
        Task<ListPackagesResponse> response = client.ListPackagesAsync(request);

        foreach (var summary in response.Result.Packages)
        {
            Package collection = new Package()
            {
                Name = summary.Package,
                Format = summary.Format.Value
            };

            Console.WriteLine($"{summary.Package}");
            var versions = client.ListPackageVersionsAsync(new ListPackageVersionsRequest()
            {
                Domain = domain,
                Repository = repository,
                Namespace = summary.Namespace,
                Package = summary.Package,
                Format = summary.Format
            });

            versions.Wait();
            foreach (var version in versions.Result.Versions)
            {
                Console.WriteLine($"{version.Version} - {version.Status}");
                collection.Versions.Add(new PackageVersion()
                {
                    Version = version.Version,
                    Revision = version.Revision
                });
            }

            packages.Add(collection);
        }

        request.NextToken = response.Result.NextToken;
        iterationCount++;
    }

    string json = packages.ToJson();
    File.WriteAllText(cache, json);
}

bool cacheDirty = false;
foreach (var package in packages)
{
    if(package.Versions.Count > 1)
    {
        Console.WriteLine($"{package.Name} has {package.Versions.Count} versions.");
        List<string> versionsToDelete = new List<string>(package.Versions.Count - 1);

        foreach(var version in package.Versions.OrderBy(x => x.Version).SkipLast(1))
        {
            versionsToDelete.Add(version.Version);
        }

        Task delete = client.DeletePackageVersionsAsync(new DeletePackageVersionsRequest()
        {
            Domain = domain,
            // Namespace = package.Namespace,
            Package = package.Name,
            Repository = repository,
            Format = package.Format,
            Versions = versionsToDelete
        });

        Console.WriteLine($"Scheduling the deletion of {package.Name} versions {string.Join(", ", versionsToDelete)}");
        delete.Wait();

        cacheDirty = true;
    }
}

if(cacheDirty)
{
    File.Delete(cache);
}