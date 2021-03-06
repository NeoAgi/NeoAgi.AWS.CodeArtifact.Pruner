using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

            // Adjust settings
            string[] repositories = Config.Repository.Split(',');
            string domain = Config.Domain;

            // Set our concurrency limit
            if (Config.ConcurrencyLimit == 0 && Environment.ProcessorCount > 1)
            {
                Config.ConcurrencyLimit = (Environment.ProcessorCount - 1) * 2;
                Logger.LogDebug("Found {processorCount}.  Using {threads} threads for concurency.", Environment.ProcessorCount, Config.ConcurrencyLimit);
            }

            if (Config.ConcurrencyLimit < 0)
            {
                Config.ConcurrencyLimit = 0;
                Logger.LogDebug("Concurrency was set less than zero.  Disabling threaded operations.");
            }

            // Ensure bounds on page limit
            if (Config.PageLimit < 1 || Config.PageLimit > 1000)
            {
                Logger.LogWarning("Page Limit was set to {limit} which is out of bounds.  Defaulting back to {default}.", Config.PageLimit, 50);
                Config.PageLimit = 50;
            }

            // Disable our checkpoint value if indicated
            if (Config.CheckpointInterval == 0)
                Config.CheckpointInterval = int.MaxValue;

            if (Config.DryRun)
                Logger.LogWarning("Dry Run is enabled.  No changes will be made.");

            // Enumerate each repository in the domain
            foreach (string repository in repositories)
            {
                Logger.LogInformation("Starting discovery for {repository}/{domain}", repository, domain);
                string? cache = (!string.IsNullOrWhiteSpace(Config.CacheLocation))
                    ? Path.Combine(Config.CacheLocation, $"package-cache-{domain}_{repository}.json")
                    : null;

                List<Package> packages = DiscoverPackages(client, cancellationToken, domain, repository, cache);

                IEnumerable<Package> versionsToRemove = await ApplyPolicyAsync(packages, domain, repository);

                await ProcessRemovalsAsync(client, cancellationToken, versionsToRemove, domain, repository, cache);
            }

            AppLifetime.StopApplication();

            await Task.CompletedTask;

            return;
        }

        private List<Package> DiscoverPackages(AmazonCodeArtifactClient client, CancellationToken cancellationToken, string domain, string repository, string? cacheFile = null)
        {
            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            bool useCache = !string.IsNullOrWhiteSpace(cacheFile);
            List<Package>? packages = new List<Package>();

            if (useCache && File.Exists(cacheFile))
            {
                try
                {
                    DateTime cacheTime = File.GetLastWriteTime(cacheFile);
                    if (cacheTime > DateTime.Now.AddHours(-1 * Config.CacheTTL))
                    {
                        string json = File.ReadAllText(cacheFile);
                        packages = JsonSerializer.Deserialize<List<Package>>(json);
                        Logger.LogDebug("API Cache created on {CacheWrittenDate} contained {count} packages.", cacheTime, packages?.Count);
                    }
                    else
                    {
                        Logger.LogInformation("Cache file at {cacheFile} is stale and wil not be used.", cacheFile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Cache File could not be used. Message: {exceptionMessage}.", ex.Message);
                }
            }

            // Reach out to the API if we have an empty structure
            if (packages?.Count == 0)
            {
                int concurrencyLimit = Config.ConcurrencyLimit;
                List<Task> tasks = new List<Task>(concurrencyLimit);
                int packageIteration = 0;
                int iterationCount = 0;
                int iterationMax = Config.PageLimit;
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
                            Logger.LogTrace("Waiting for threads...");
                            Task t = Task.WhenAny(tasks.ToArray());
                            tasks.Remove(t);
                        }

                        if (packageIteration % Config.CheckpointInterval == 0)
                            Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}", packageIteration, domain, repository);

                        packageIteration++;
                    }

                    request.NextToken = response.Result.NextToken;
                    iterationCount++;
                }

                // Block until we're complete
                Logger.LogDebug("Waiting for completion...");
                Task.WaitAll(tasks.ToArray());

                if (useCache)
                {
                    try
                    {
                        string json = JsonSerializer.Serialize(packages);
#pragma warning disable CS8604 // Possible null reference argument.
                        File.WriteAllText(cacheFile, json);
#pragma warning restore CS8604 // Possible null reference argument.
                        Logger.LogDebug("Wrote {bytes}b to {cacheLocation}", json.Length, cacheFile);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Cache File could not be written.  Message: {exceptionMessage}", ex.Message);
                    }
                }
            }

            Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}.", packages?.Count, domain, repository);

            return packages ?? new List<Package>(0);
        }

        protected async Task DiscoverPackageVersionsAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, Package package, PackageSummary summary, string domain, string repository)
        {
            await Task.Factory.StartNew(() =>
            {
                Logger.LogDebug("Retrieving package information for {packageName} on {domain}/{repository}", summary.Package, domain, repository);
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
                    Logger.LogTrace("{packageName} - {packageVersion} - {packageStatus}", package.Name, version.Version, version.Status);
                    package.Versions.Add(new PackageVersion()
                    {
                        Version = version.Version,
                        Revision = version.Revision
                    });
                }

                Logger.LogDebug("Discovered {packageName} from {domain}/{repository} with {versionCount} versions.", package.Name, domain, repository, package.Versions.Count);
            });
        }

        public async Task<IEnumerable<Package>> ApplyPolicyAsync(List<Package> packages, string domain, string repository)
        {
            PolicyManager<Package> manager = new PolicyManager<Package>();
            manager.Policies.Add(new PersistVersionCount("NeoAgi*", int.MaxValue));
            manager.Policies.Add(new PersistVersionCount("*", 3));

            Logger.LogInformation("Applying {policyCount} policies across {packageCount} packages on {domain}/{repository}", manager.Policies.Count, packages.Count, domain, repository);

            return await manager.OutOfPolicyAsync(packages);
        }

        public async Task ProcessRemovalsAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, IEnumerable<Package> packages, string domain, string repository, string? cacheFile = null)
        {
            if (packages.Count() > 0)
            {
                Logger.LogDebug("Scheduling the deletion of {packageCount} package(s) from {domain}/{repository}", packages.Count(), domain, repository);

                await Task.Run(async () =>
                {
                    List<Task> removalTasks = new List<Task>();
                    bool cacheDirty = false;
                    foreach (Package package in packages)
                    {
                        Task remove = RemovePackageVersionAsync(client, cancellationToken, domain, repository, package.Name, package.Format, package.Versions);

                        cacheDirty = true;
                    }

                    if (cacheFile != null && cacheDirty)
                    {
                        File.Delete(cacheFile);
                        Logger.LogDebug("CacheFile Dirty.  Removing {cacheFile}", cacheFile);
                    }

                    Task.WaitAll(removalTasks.ToArray());
                    Logger.LogTrace("Waiting for removal tasks to complete...");

                    return Task.CompletedTask;
                });
            }
            else
            {
                Logger.LogInformation("No packages found for deletion on {domain}/{repository}.", domain, repository);
                return;
            }
        }

        public async Task RemovePackageVersionAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, string domain, string repository, string packageName, string packageFormat, List<PackageVersion> versionsToDelete)
        {
            Logger.LogInformation("Scheduling the deletion of {packageName} from {domain}/{repository} version(s): {versionsToDelete}.", packageName, domain, repository, string.Join(", ", versionsToDelete.Select(x => x.Version)));

            if (!Config.DryRun)
            {
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
            }

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
