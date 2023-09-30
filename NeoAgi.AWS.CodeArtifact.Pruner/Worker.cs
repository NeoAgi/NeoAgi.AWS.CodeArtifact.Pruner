using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;
using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentQueue<QueuedAction> ActionQueue = new ConcurrentQueue<QueuedAction>();

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

                List<Package> packages = await DiscoverPackagesAsync(client, cancellationToken, domain, repository);

                await ProcessQueueAsync(client, cancellationToken, domain, repository);

                IEnumerable<Package> versionsToRemove = await ApplyPolicyAsync(packages, domain, repository);

                await ProcessRemovalsAsync(client, cancellationToken, versionsToRemove, domain, repository);
            }

            AppLifetime.StopApplication();

            await Task.CompletedTask;

            return;
        }

        private async Task<List<Package>> DiscoverPackagesAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, string domain, string repository)
        {
            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            List<Package>? packages = new List<Package>();

            // Reach out to the API if we have an empty structure
            if (packages?.Count == 0)
            {
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

                        await DiscoverPackageVersionsAsync(client, cancellationToken, collection, summary, domain, repository);

                        if (packageIteration % Config.CheckpointInterval == 0)
                            Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}", packageIteration, domain, repository);

                        packageIteration++;
                    }

                    request.NextToken = response.Result.NextToken;
                    iterationCount++;
                }
            }

            Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}.", packages?.Count, domain, repository);

            return packages ?? new List<Package>(0);
        }

        private async Task ProcessQueueAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, string domain, string repository)
        {
            while (!ActionQueue.IsEmpty)
            {
                QueuedAction? action;
                if (ActionQueue.TryDequeue(out action) && action != null)
                {
                    if (action is QueuedActionGetPackageVersion)
                    {

                    }
                    else if (action is QueuedActionDeleteVersion)
                    {

                    }
                }
            }
        }

        protected async Task DiscoverPackageVersionsAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, Package package, PackageSummary summary, string domain, string repository)
        {
            Logger.LogDebug("Retrieving package information for {packageName} on {domain}/{repository}", summary.Package, domain, repository);
            ListPackageVersionsResponse versions = await client.ListPackageVersionsAsync(new ListPackageVersionsRequest()
            {
                Domain = domain,
                Repository = repository,
                Namespace = summary.Namespace,
                Package = summary.Package,
                Format = summary.Format
            }, cancellationToken);

            foreach (PackageVersionSummary version in versions.Versions)
            {
                Logger.LogTrace("{packageName} - {packageVersion} - {packageStatus}", package.Name, version.Version, version.Status);
                package.Versions.Add(new PackageVersion()
                {
                    Version = version.Version,
                    Revision = version.Revision
                });
            }

            Logger.LogDebug("Discovered {packageName} from {domain}/{repository} with {versionCount} versions.", package.Name, domain, repository, package.Versions.Count);
        }

        public async Task<IEnumerable<Package>> ApplyPolicyAsync(List<Package> packages, string domain, string repository)
        {
            PolicyManager<Package> manager = new PolicyManager<Package>();
            manager.Policies.Add(new PersistVersionCount("NeoAgi*", int.MaxValue));
            manager.Policies.Add(new PersistVersionCount("*", 3));

            Logger.LogInformation("Applying {policyCount} policies across {packageCount} packages on {domain}/{repository}", manager.Policies.Count, packages.Count, domain, repository);

            return await manager.OutOfPolicyAsync(packages);
        }

        public async Task ProcessRemovalsAsync(AmazonCodeArtifactClient client, CancellationToken cancellationToken, IEnumerable<Package> packages, string domain, string repository)
        {
            if (packages.Count() > 0)
            {
                Logger.LogDebug("Scheduling the deletion of {packageCount} package(s) from {domain}/{repository}", packages.Count(), domain, repository);

                List<Task> removalTasks = new List<Task>();
                foreach (Package package in packages)
                {
                    await RemovePackageVersionAsync(client, cancellationToken, domain, repository, package.Name, package.Format, package.Versions);
                }

                Task.WaitAll(removalTasks.ToArray());
                Logger.LogTrace("Waiting for removal tasks to complete...");
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
