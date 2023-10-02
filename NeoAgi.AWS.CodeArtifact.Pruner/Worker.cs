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

        private AmazonCodeArtifactClient Client { get; set; }
        private PolicyManager PackagePolicyManager { get; set; } = new PolicyManager();

        private int TPS = 0;

        public Worker(ILogger<Worker> logger, IOptions<PrunerConfig> config, AmazonCodeArtifactClient codeArtifactClient, IHostApplicationLifetime appLifetime)
        {
            Logger = logger;
            Client = codeArtifactClient;
            AppLifetime = appLifetime;
            Config = config.Value;

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Attach our policies to the Manager
            PackagePolicyManager.Policies.Add(new PersistVersionCount("NeoAgi*", int.MaxValue));
            PackagePolicyManager.Policies.Add(new PersistVersionCount("*", 3));


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

                // Discover packages in the domain/repository
                await DiscoverPackagesAsync(cancellationToken, domain, repository);

                // Process the queue
                await ProcessQueueAsync(cancellationToken);
            }

            AppLifetime.StopApplication();
        }

        internal async Task DiscoverPackagesAsync(CancellationToken cancellationToken, string domain, string repository)
        {
            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            int packageIteration = 0;
            int iterationCount = 0;
            int iterationMax = Config.PageLimit;
            while (iterationCount == 0 || (request.NextToken != null && iterationCount < iterationMax))
            {
                ListPackagesResponse response = await Client.ListPackagesAsync(request, cancellationToken);

                foreach (PackageSummary summary in response.Packages)
                {
                    ActionQueue.Enqueue(new QueuedActionGetPackageVersions(new Package(domain, repository, summary)));

                    if (packageIteration % Config.CheckpointInterval == 0)
                        Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}", packageIteration, domain, repository);

                    packageIteration++;
                }

                request.NextToken = response.NextToken;
                iterationCount++;
            }

            Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}.", packageIteration, domain, repository);
        }

        internal async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            // Start a thread to clear out our TPS counter
            _ = Task.Factory.StartNew(() => StartTPSCounter(cancellationToken));

            int totalTasks = 10;
            List<Task> tasks = new List<Task>(totalTasks);

            while (!ActionQueue.IsEmpty)
            {
                QueuedAction? action;
                if (ActionQueue.TryDequeue(out action) && action != null)
                {
                    if (action is QueuedActionGetPackageVersions)
                    {
                        tasks.Add(DiscoverPackageVersionsAsync(cancellationToken, (QueuedActionGetPackageVersions)action));
                        TPS++;
                    }
                    else if (action is QueuedActionDeleteVersion)
                    {
                        tasks.Add(RemovePackageVersionAsync(cancellationToken, (QueuedActionDeleteVersion)action));
                        TPS++;
                    }

                    // See if we're over our TPS
                    if (TPS >= 20)
                    {
                        Logger.LogWarning("Reached TPS of 20... blocking... ");
                        Task.Delay(1000).Wait();
                    }

                    // Stop here and see how many transactions are currently running
                    if (tasks.Count >= totalTasks)
                    {
                        Task<Task> finished = Task.WhenAny(tasks);
                        finished.Wait();

                        tasks.Remove(finished);
                    }
                }
            }
        }

        internal async Task StartTPSCounter(CancellationToken cancellationToken)
        {
            for (int i = 0; i < int.MaxValue; i++)
            {
                Logger.LogWarning("TPS is {tps}", TPS);
                TPS = 0;

                Task timeout = Task.Delay(1000);
                timeout.Wait();
            }
        }

        internal async Task DiscoverPackageVersionsAsync(CancellationToken cancellationToken, QueuedActionGetPackageVersions action)
        {
            Package package = action.Package;

            Logger.LogDebug("Retrieving package information for {packageName} on {domain}/{repository}", package.Name, package.Domain, package.Repository);
            ListPackageVersionsResponse versions = await Client.ListPackageVersionsAsync(new ListPackageVersionsRequest()
            {
                Domain = package.Domain,
                Repository = package.Repository,
                Namespace = package.Namespace,
                Package = package.Name,
                Format = package.Format
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

            // Apply out policy
            var versionsToRemove = PackagePolicyManager.VersionsOutOfPolicy(package);

            Logger.LogDebug("Discovered {packageName} from {domain}/{repository} with {versionCount} versions.  {versionsOutOfPolicy} are out of policy."
                , package.Name, package.Domain, package.Repository, package.Versions.Count, versionsToRemove.Count());

            // If we have versions ouf of policy, enqueue the removal
            if (versionsToRemove.Count() > 0)
                ActionQueue.Enqueue(new QueuedActionDeleteVersion(package, versionsToRemove));
        }

        internal async Task RemovePackageVersionAsync(CancellationToken cancellationToken, QueuedActionDeleteVersion action)
        {
            Package package = action.Package;
            IEnumerable<PackageVersion> versions = action.Versions;

            Logger.LogInformation("Deleting {packageName}@{versionsToDelete} from {domain}/{repository}."
                , package.Name, string.Join(", ", versions.Select(x => x.Version)), package.Domain, package.Repository);

            if (!Config.DryRun)
            {
                List<string> versionsToDelete = new List<string>();
                foreach (var version in versions)
                {
                    versionsToDelete.Add(version.Version);
                }

                await Client.DeletePackageVersionsAsync(new DeletePackageVersionsRequest()
                {
                    Domain = package.Domain,
                    Namespace = package.Namespace,
                    Package = package.Name,
                    Repository = package.Repository,
                    Format = package.Format,
                    Versions = versionsToDelete
                }, cancellationToken);
            }

            return;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        private void OnStarted() { }
        private void OnStopping() { }
        private void OnStopped() { }
    }
}
