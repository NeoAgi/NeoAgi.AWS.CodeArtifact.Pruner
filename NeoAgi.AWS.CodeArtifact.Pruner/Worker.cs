using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;
using NeoAgi.AWS.CodeArtifact.Pruner.Policies;
using NeoAgi.Threading.RateLimiting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            // Adjust settings
            string[] repositories = Config.Repository.Split(',');
            string domain = Config.Domain;

            // Ensure bounds on page limit
            if (Config.PageLimit < 1 || Config.PageLimit > 1000)
            {
                Logger.LogWarning("Page Limit was set to {limit} which is out of bounds.  Defaulting back to {default}.", Config.PageLimit, 50);
                Config.PageLimit = 50;
            }

            // Ensure we have a positive number for Parallism
            if (Config.Parallalism < 1)
            {
                Logger.LogWarning("Parallelism must be set to a positive integer.  Received {parallismValue}.  Setting to 1 (disables threading).", Config.Parallalism);
                Config.Parallalism = 1;
            }

            // Ensure our TPS is a positive number
            if (Config.MaxTransactionsPerSecond < 1)
            {
                Logger.LogWarning("Maximum Transactions Per Second must be a positive number.  Received {tpsValue}.  Disabling throttling.", Config.MaxTransactionsPerSecond);
                Config.MaxTransactionsPerSecond = int.MaxValue;
            }

            // Disable our checkpoint value if indicated
            if (Config.CheckpointInterval == 0)
                Config.CheckpointInterval = int.MaxValue;

            if (Config.DryRun)
                Logger.LogWarning("Dry Run is enabled.  No changes will be made.");

            if (string.IsNullOrEmpty(Config.Policy))
                throw new ApplicationException("Policy cannot be empty");

            try
            {
                // Attach our policies to the Manager
                PersistVersionCount[]? policies = JsonSerializer.Deserialize<PersistVersionCount[]>(Config.Policy, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });

                if (policies == null || policies.Length == 0)
                    throw new ApplicationException("Policy document could not be parsed.");

                PackagePolicyManager.Policies.AddRange(policies);

                // Look to see if we have a default policy
                bool defaultPolicyFound = false;
                foreach (var policy in policies)
                {
                    if (policy.Namespace.Equals("*"))
                    {
                        defaultPolicyFound = true;
                    }

                    Logger.LogDebug("Found Policy: Namespace {namespace}, Versions to Keep {versionCount}", policy.Namespace, policy.VersionsToKeep);
                }

                if (!defaultPolicyFound)
                {
                    PackagePolicyManager.Policies.Add(new PersistVersionCount("*", 100));
                    Logger.LogWarning("Policy provided did not contain a default rule.  Adding a default rule to keep last 100 versions.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to parse Policy Document.  Message: {message}", ex.Message);
                AppLifetime.StopApplication();
                return;
            }

            Logger.LogInformation("Policy document contained {policyCount} entries.", PackagePolicyManager.Policies.Count);

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
            _ = StartTPSCounterAsync(cancellationToken);

            int totalActionCount = 0;
            Stopwatch sw = Stopwatch.StartNew();

            await ActionQueue.ProcessWithTPSAsync(Config.MaxTransactionsPerSecond, Config.Parallalism, async (action) =>
            {
                // Before Processing verify our re-attempt queue
                if (action.AttemptedCount > 2)
                {
                    Logger.LogWarning("[{actionId}] Attempted action count exhausted.  Will not re-attempt.", action.ActionID);
                    DeadActionQueue.Enqueue(action);
                }
                else
                {

                    // Route our action to the correct handler
                    if (action is QueuedActionGetPackageVersions)
                    {
                        await DiscoverPackageVersionsAsync(cancellationToken, (QueuedActionGetPackageVersions)action);
                    }
                    else if (action is QueuedActionDeleteVersion)
                    {
                        await RemovePackageVersionAsync(cancellationToken, (QueuedActionDeleteVersion)action);
                    }
                    else if (action is QueuedActionDiscoverPackages)
                    {
                        await DiscoverPackagesAsync(cancellationToken, (QueuedActionDiscoverPackages)action);
                    }

                    // Increment our TPS and Backoff Counters
                    TPS++;

                    // Increment our attempted value
                    action.IncreaseAttempted();

                    totalActionCount++;

                    if (totalActionCount % Config.CheckpointInterval == 0)
                    {
                        Logger.LogInformation("ActionQueue has processed {actionCount} in {durration} seconds.  {queueDepth} items are queued. {deadQueueDepth} dead actions encountered."
                            , totalActionCount, Math.Round((decimal)(sw.ElapsedMilliseconds) / 1000, 2), ActionQueue.Count, DeadActionQueue.Count);
                    }
                }
            }, cancellationToken);
        }

        internal async Task StartTPSCounterAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < int.MaxValue; i++)
            {
                Logger.LogTrace("TPS is {tps}.  ActionQueue Depth is {queueDepth}", TPS, ActionQueue.Count);
                TPS = 0;

                await Task.Delay(1000);
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
            else
            {
                Logger.LogInformation("[{actionId}] Dry Run Mode Enabled.  Would have removed {packageName}@{versionsToDelete} from {domain}/{repository}."
                , action.ActionID, package.Name, string.Join(", ", versions.Select(x => x.Version)), package.Domain, package.Repository);
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
