﻿using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeoAgi.Runtime.Serialization.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner
{
    public class Worker : IHostedService
    {
        private readonly ILogger Logger;
        private readonly IHostApplicationLifetime AppLifetime;

        public Worker(ILogger<Worker> logger, IHostApplicationLifetime appLifetime)
        {
            Logger = logger;
            AppLifetime = appLifetime;

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation("1. StartAsync has been called.");

            var client = new AmazonCodeArtifactClient(new AmazonCodeArtifactConfig()
            {
                RegionEndpoint = Amazon.RegionEndpoint.USWest2,
            });

            string domain = "neoagi";
            string repository = "nuget-store";
            string cache = $"h:\\package-cache-{domain}_{repository}.json";

            ListPackagesRequest request = new ListPackagesRequest()
            {
                Domain = domain,
                Repository = repository
            };

            List<Package> packages = new List<Package>();

            if (File.Exists(cache))
            {
                DateTime cacheTime = File.GetLastWriteTime(cache);
                if (cacheTime > DateTime.Now.AddHours(-8))
                {
                    string json = File.ReadAllText(cache);
                    packages = json.FromJson<List<Package>>();
                    Logger.LogDebug("API Cache created on {CacheWrittenDate} contained {count} packages.", cacheTime, packages.Count);
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

                        Logger.LogInformation($"{summary.Package}");
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
                            Logger.LogInformation($"{version.Version} - {version.Status}");
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
                if (package.Versions.Count > 1)
                {
                    Logger.LogInformation($"{package.Name} has {package.Versions.Count} versions.");
                    List<string> versionsToDelete = new List<string>(package.Versions.Count - 1);

                    foreach (var version in package.Versions.OrderBy(x => x.Version).SkipLast(1))
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

                    Logger.LogInformation($"Scheduling the deletion of {package.Name} versions {string.Join(", ", versionsToDelete)}");
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
            Logger.LogInformation("4. StopAsync has been called.");

            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            Logger.LogInformation("2. OnStarted has been called.");
        }

        private void OnStopping()
        {
            Logger.LogInformation("3. OnStopping has been called.");
        }

        private void OnStopped()
        {
            Logger.LogInformation("5. OnStopped has been called.");
        }
    }
}