using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NeoAgi.Runtime.Serialization.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace NeoAgi.AWS.CodeArtifact.Pruner
{
    public class Worker : IHostedService
    {
        private readonly ILogger Logger;
        private readonly IHostApplicationLifetime AppLifetime;
        private readonly PrunerConfig Config;

        public Worker(ILogger<Worker> logger, IOptions<PrunerConfig> config, IHostApplicationLifetime appLifetime)
        {
            Logger = logger;
            AppLifetime = appLifetime;
            Config = config.Value;

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var client = new AmazonCodeArtifactClient(new AmazonCodeArtifactConfig()
            {
                RegionEndpoint = Amazon.RegionEndpoint.USWest2
            });

            string domain = Config.Domain;
            string repository = Config.Repository;
            string cache = Path.Combine(Config.CacheLocation, $"package-cache-{domain}_{repository}.json");

            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            List<Package> packages = new List<Package>();

            if (File.Exists(cache))
            {
                try
                {
                    DateTime cacheTime = File.GetLastWriteTime(cache);
                    if (cacheTime > DateTime.Now.AddHours(-1 * Config.CacheTTL))
                    {
                        string json = File.ReadAllText(cache);
                        packages = json.FromJson<List<Package>>();
                        Logger.LogDebug("API Cache created on {CacheWrittenDate} contained {count} packages.", cacheTime, packages.Count);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Cache File could not be used. Message: {exceptionMessage}.", ex.Message);
                }
            }
            else
            {
                Logger.LogDebug("API Cache was missing or stale.  Skipping.");
            }

            // Reach out to the API if we have an empty structure
            if (packages.Count == 0)
            {
                int iterationCount = 0;
                int iterationMax = 50;
                while (iterationCount == 0 || (request.NextToken != null && iterationCount < iterationMax))
                {
                    Task<ListPackagesResponse> response = client.ListPackagesAsync(request);

                    foreach (PackageSummary summary in response.Result.Packages)
                    {
                        Package collection = new Package()
                        {
                            Name = summary.Package,
                            Format = summary.Format.Value
                        };

                        Task<ListPackageVersionsResponse> versions = client.ListPackageVersionsAsync(new ListPackageVersionsRequest()
                        {
                            Domain = domain,
                            Repository = repository,
                            Namespace = summary.Namespace,
                            Package = summary.Package,
                            Format = summary.Format
                        });

                        versions.Wait();
                        foreach (PackageVersionSummary version in versions.Result.Versions)
                        {
                            Logger.LogInformation(" {packageName} - {packageVersion} - {packageStatus}", collection.Name, version.Version, version.Status);
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

                try
                {
                    string json = packages.ToJson();
                    File.WriteAllText(cache, json);
                    Logger.LogDebug("Wrote {bytes}b to {cacheLocation}", json.Length, cache);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Cache File could not be written.  Message: {exceptionMessage}", ex.Message);
                }
            }

            bool cacheDirty = false;
            foreach (var package in packages)
            {
                if (package.Versions.Count > 1)
                {
                    Logger.LogInformation($"{package.Name} has {package.Versions.Count} versions.");
                    List<string> versionsToDelete = new List<string>(package.Versions.Count - 1);

                    int packageItteration = 0;
                    string versionToKeep = string.Empty;
                    foreach (var version in package.Versions.OrderBy(x => x.Version))
                    {
                        packageItteration++;
                        if (packageItteration == package.Versions.Count)
                        {
                            versionToKeep = version.Version;
                        }
                        else
                        {
                            versionsToDelete.Add(version.Version);
                        }
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

                    Logger.LogInformation("Scheduling the deletion of {packageName} versions {versionsToDelete}.  Keeping {versionToKeep}", package.Name, versionsToDelete, versionToKeep);
                    delete.Wait();

                    cacheDirty = true;
                }
            }

            if (cacheDirty)
            {
                File.Delete(cache);
            }

            AppLifetime.StopApplication();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStarted() { }
        private void OnStopping() { }
        private void OnStopped() { }
    }
}
