using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly ConcurrentQueue<QueuedAction> DeadActionQueue = new ConcurrentQueue<QueuedAction>();

        private AmazonCodeArtifactClient Client { get; set; }
        private PolicyManager PackagePolicyManager { get; set; } = new PolicyManager();

        private int TPS = 0;
        private Dictionary<string, int> TotalPackages = new Dictionary<string, int>();

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
            }

            // Process the queue
            Task t = ProcessQueueAsync(cancellationToken);
            t.Wait();

            AppLifetime.StopApplication();
        }

        internal async Task DiscoverPackagesAsync(CancellationToken cancellationToken, QueuedActionDiscoverPackages action)
        {
            try
            {
                await DiscoverPackagesAsync(cancellationToken, action.Domain, action.Repository, action.PageIterationCount, action.NextToken);
            }
            catch (ThrottlingException)
            {
                Logger.LogWarning("[{actionId}] Throttled by Upstream Service.  Requeuing package discovery for {domain}/{repository}."
                    , action.ActionID, action.Domain, action.Repository);

                // Requeue the attempt
                ActionQueue.Enqueue(action);
            }
        }

        internal async Task DiscoverPackagesAsync(CancellationToken cancellationToken, string domain, string repository, int pageIterationCount = 0, string? nextToken = null)
        {
            if (pageIterationCount > Config.PageLimit)
            {
                Logger.LogWarning("Reached the maxiumum number of pages.  Increase '--pageLimit' beyond {pageLimit}.", Config.PageLimit);
                return;
            }

            if (pageIterationCount != 0 && string.IsNullOrEmpty(nextToken))
            {
                Logger.LogError("Received an empty pageToken with a non-zero page count.  Exiting discovery action.");
                return;
            }

            // Continue with the discovery
            Logger.LogInformation("Retriving next page of packages.  Page count is {pageCount}.", pageIterationCount);

            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            if (nextToken != null)
                request.NextToken = nextToken;

            string packageKey = domain + repository;
            if (!TotalPackages.ContainsKey(packageKey))
                TotalPackages[packageKey] = 0;

            int packageIteration = TotalPackages[packageKey];

            ListPackagesResponse response = await Client.ListPackagesAsync(request, cancellationToken);

            foreach (PackageSummary summary in response.Packages)
            {
                ActionQueue.Enqueue(new QueuedActionGetPackageVersions(new Package(domain, repository, summary)));

                if (packageIteration % Config.CheckpointInterval == 0)
                    Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository}", packageIteration, domain, repository);

                packageIteration++;
            }

            TotalPackages[packageKey] = packageIteration;

            if (response.NextToken != null)
            {
                pageIterationCount++;
                Logger.LogInformation("Next Token found.  Scheduling call of page {pageCount}.", pageIterationCount);
                ActionQueue.Enqueue(new QueuedActionDiscoverPackages(domain, repository, pageIterationCount, response.NextToken));
            }

            Logger.LogInformation("Discovered {packageCount} packages from {domain}/{repository} on page {pageItteration}.", packageIteration, domain, repository, pageIterationCount);
        }

        internal async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            // Start a thread to clear out our TPS counter
            _ = Task.Factory.StartNew(() => StartTPSCounter(cancellationToken));

            int totalActionCount = 0;
            int tps = 20;
            int backoffTps = 0;
            int totalTasks = 10;
            int tpsRatio = (int)(1000 / tps);
            List<Task> tasks = new List<Task>(totalTasks);
            Stopwatch sw = Stopwatch.StartNew();

            while (!ActionQueue.IsEmpty)
            {
                QueuedAction? action;
                if (ActionQueue.TryDequeue(out action) && action != null)
                {
                    // Before Processing verify our re-attempt queue
                    if (action.AttemptedCount > 2)
                    {
                        Logger.LogWarning("[{actionId}] Attempted action count exhausted.  Will not re-attempt.", action.ActionID);
                        DeadActionQueue.Enqueue(action);
                        continue;
                    }

                    // Route our action to the correct handler
                    if (action is QueuedActionGetPackageVersions)
                    {
                        tasks.Add(DiscoverPackageVersionsAsync(cancellationToken, (QueuedActionGetPackageVersions)action));
                    }
                    else if (action is QueuedActionDeleteVersion)
                    {
                        tasks.Add(RemovePackageVersionAsync(cancellationToken, (QueuedActionDeleteVersion)action));
                    }
                    else if (action is QueuedActionDiscoverPackages)
                    {
                        tasks.Add(DiscoverPackagesAsync(cancellationToken, (QueuedActionDiscoverPackages)action));
                    }

                    // Increment our TPS and Backoff Counters
                    TPS++; backoffTps++;

                    // Increment our attempted value
                    action.IncreaseAttempted();

                    // If we've reached our TPS counter, ramp up out speed
                    if (backoffTps <= tps)
                    {
                        Logger.LogTrace("Throttling TPS rampup.  Waiting {tpsRampTime}ms", tpsRatio);
                        Task.Delay(tpsRatio).Wait();
                    }

                    // See if we're over our TPS
                    if (TPS >= tps)
                    {
                        Logger.LogTrace("Maximum TPS reached.  Waiting...");
                        Task.Delay(1000).Wait();
                        backoffTps = 0;
                    }

                    // Stop here and see how many transactions are currently running
                    if (tasks.Count >= totalTasks)
                    {
                        Task<Task> finished = Task.WhenAny(tasks);
                        finished.Wait();

                        tasks.Remove(finished);
                    }

                    totalActionCount++;

                    if (totalActionCount % Config.CheckpointInterval == 0)
                    {
                        Logger.LogWarning("ActionQueue has processed {actionCount} in {durration} seconds.  {queueDepth} items are queued. {deadQueueDepth} dead actions encountered."
                            , totalActionCount, Math.Round((decimal)(sw.ElapsedMilliseconds) / 1000, 2), ActionQueue.Count, DeadActionQueue.Count);
                    }
                }
            }
        }

        internal async Task StartTPSCounter(CancellationToken cancellationToken)
        {
            for (int i = 0; i < int.MaxValue; i++)
            {
                Logger.LogTrace("TPS is {tps}.  ActionQueue Depth is {queueDepth}", TPS, ActionQueue.Count);
                TPS = 0;

                Task timeout = Task.Delay(1000);
                timeout.Wait();
            }
        }

        internal async Task DiscoverPackageVersionsAsync(CancellationToken cancellationToken, QueuedActionGetPackageVersions action)
        {
            Package package = action.Package;

            Logger.LogDebug("[{actionId}] Retrieving package information for {packageName} on {domain}/{repository}"
                , action.ActionID, package.Name, package.Domain, package.Repository);

            try
            {
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
                    Logger.LogTrace("[{actionId}] {packageName} - {packageVersion} - {packageStatus}", action.ActionID, package.Name, version.Version, version.Status);
                    package.Versions.Add(new PackageVersion()
                    {
                        Version = version.Version,
                        Revision = version.Revision
                    });
                }

                // Apply out policy
                var versionsToRemove = PackagePolicyManager.VersionsOutOfPolicy(package);

                Logger.LogDebug("[{actionId}] Discovered {packageName} from {domain}/{repository} with {versionCount} versions.  {versionsOutOfPolicy} are out of policy."
                    , action.ActionID, package.Name, package.Domain, package.Repository, package.Versions.Count, versionsToRemove.Count());

                // If we have versions ouf of policy, enqueue the removal
                if (versionsToRemove.Count() > 0)
                    ActionQueue.Enqueue(new QueuedActionDeleteVersion(package, versionsToRemove));
            }
            catch (ThrottlingException)
            {
                Logger.LogWarning("[{actionId}] Throttled by Upstream Service.  Requeuing version discovery for package {package} in {domain}/{repository}."
                    , action.ActionID, package.Name, package.Domain, package.Repository);

                // Requeue the attempt
                ActionQueue.Enqueue(action);
            }
        }

        internal async Task RemovePackageVersionAsync(CancellationToken cancellationToken, QueuedActionDeleteVersion action)
        {
            Package package = action.Package;
            IEnumerable<PackageVersion> versions = action.Versions;

            Logger.LogInformation("[{actionId}] Deleting {packageName}@{versionsToDelete} from {domain}/{repository}."
                , action.ActionID, package.Name, string.Join(", ", versions.Select(x => x.Version)), package.Domain, package.Repository);

            if (!Config.DryRun)
            {
                List<string> versionsToDelete = new List<string>();
                foreach (var version in versions)
                {
                    versionsToDelete.Add(version.Version);
                }

                try
                {
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
                catch (ThrottlingException)
                {
                    Logger.LogWarning("[{actionId}] Throttled by Upstream Service.  Requeuing package version deletion for package {package} in {domain}/{repository}."
                        , action.ActionID, package.Name, package.Domain, package.Repository);

                    // Requeue the attempt
                    ActionQueue.Enqueue(action);
                }
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
