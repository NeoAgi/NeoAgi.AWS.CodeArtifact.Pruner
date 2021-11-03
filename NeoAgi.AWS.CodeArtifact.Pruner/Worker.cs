using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NeoAgi.Text.Json;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;

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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            AmazonCodeArtifactClient client = new AmazonCodeArtifactClient(new AmazonCodeArtifactConfig()
            {
                RegionEndpoint = Amazon.RegionEndpoint.USWest2
            });

            string domain = Config.Domain;
            string repository = Config.Repository;
            string cache = Path.Combine(Config.CacheLocation, $"package-cache-{domain}_{repository}.json");

            List<Package> packages = DiscoverPackages(client, cancellationToken, domain, repository, cache);

            Task<IEnumerable<Package>> versionsToRemove = ApplyPolicyAsync(packages);

            ProcessRemovals(client, cancellationToken, versionsToRemove.Result, domain, repository, cache);

            AppLifetime.StopApplication();

            await Task.CompletedTask;

            return;
        }

        private List<Package> DiscoverPackages(AmazonCodeArtifactClient client, CancellationToken cancellationToken, string domain, string repository, string cacheFile)
        {
            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            List<Package> packages = new List<Package>();

            if (!string.IsNullOrEmpty(cacheFile) && File.Exists(cacheFile))
            {
                try
                {
                    DateTime cacheTime = File.GetLastWriteTime(cacheFile);
                    if (cacheTime > DateTime.Now.AddHours(-1 * Config.CacheTTL))
                    {
                        string json = File.ReadAllText(cacheFile);
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
                int concurrencyLimit = 20;
                List<Task> tasks = new List<Task>(concurrencyLimit);
                int iterationCount = 0;
                int iterationMax = 50;
                while (iterationCount == 0 || (request.NextToken != null && iterationCount < iterationMax))
                {
                    Task<ListPackagesResponse> response = client.ListPackagesAsync(request, cancellationToken);

                    foreach (PackageSummary summary in response.Result.Packages)
                    {
                        Package collection = new Package()
                        {
                            Name = summary.Package,
                            Format = summary.Format.Value
                        };

                        packages.Add(collection);

                        tasks.Add(DiscoverPackageVersionsAsync(client, cancellationToken, collection, summary, domain, repository));

                        // Throttle the task queue a bit
                        if (tasks.Count > concurrencyLimit)
                        {
                            Logger.LogDebug("Waiting for threads...");
                            Task t = Task.WhenAny(tasks.ToArray());
                            tasks.Remove(t);
                        }
                    }

                    request.NextToken = response.Result.NextToken;
                    iterationCount++;
                }

                // Block until we're complete
                Logger.LogDebug("Waiting for completion...");
                Task.WaitAll(tasks.ToArray());

                try
                {
                    string json = packages.ToJson();
                    File.WriteAllText(cacheFile, json);
                    Logger.LogDebug("Wrote {bytes}b to {cacheLocation}", json.Length, cacheFile);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Cache File could not be written.  Message: {exceptionMessage}", ex.Message);
                }
            }

            return packages;
        }

        protected async Task DiscoverPackageVersionsAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, Package package, PackageSummary summary, string domain, string repository)
        {
            await Task.Factory.StartNew(() =>
            {
                Task<ListPackageVersionsResponse> versions = client.ListPackageVersionsAsync(new ListPackageVersionsRequest()
                {
                    Domain = domain,
                    Repository = repository,
                    Namespace = summary.Namespace,
                    Package = summary.Package,
                    Format = summary.Format
                }, cancellationToken);

                foreach (PackageVersionSummary version in versions.Result.Versions)
                {
                    Logger.LogDebug("{packageName} - {packageVersion} - {packageStatus}", package.Name, version.Version, version.Status);
                    package.Versions.Add(new PackageVersion()
                    {
                        Version = version.Version,
                        Revision = version.Revision
                    });
                }

                Logger.LogInformation("Discovered {packageName} with {versionCount} versions.", package.Name, package.Versions.Count);
            });
        }

        public async Task<IEnumerable<Package>> ApplyPolicyAsync(List<Package> packages)
        {
            PolicyManager<Package> manager = new PolicyManager<Package>();
            manager.Policies.Add(new PersistVersionCount("NeoAgi*", int.MaxValue));
            manager.Policies.Add(new PersistVersionCount("*", 2));

            return await manager.OutOfPolicyAsync(packages);
        }

        public void ProcessRemovals(AmazonCodeArtifactClient client, CancellationToken cancellationToken, IEnumerable<Package> packages, string domain, string repository, string cacheFile)
        {
            bool cacheDirty = false;
            foreach (Package package in packages)
            {
                Task delete = RemovePackageVersionAsync(client, cancellationToken, domain, repository, package.Name, package.Format, package.Versions);

                delete.Wait();

                cacheDirty = true;
            }

            if (cacheDirty)
            {
                File.Delete(cacheFile);
                Logger.LogDebug("CacheFile Dirty.  Removing {cacheFile}", cacheFile);
            }
        }

        public async Task RemovePackageVersionAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, string domain, string repository, string packageName, string packageFormat, List<PackageVersion> versionsToDelete)
        {
            Logger.LogInformation("Scheduling the deletion of {packageName} versions {versionsToDelete}.", packageName, string.Join(", ", versionsToDelete));

            List<string> versionsToDeleteString = new List<string>();
            foreach (PackageVersion version in versionsToDelete)
            {
                versionsToDeleteString.Add(version.Version);
            }

            await client.DeletePackageVersionsAsync(new DeletePackageVersionsRequest()
            {
                Domain = domain,
                // Namespace = package.Namespace,
                Package = packageName,
                Repository = repository,
                Format = packageFormat,
                Versions = versionsToDeleteString
            }, cancellationToken);

            return;
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
