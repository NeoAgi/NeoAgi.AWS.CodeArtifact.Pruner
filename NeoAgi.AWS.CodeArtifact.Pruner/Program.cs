// See https://aka.ms/new-console-template for more information

using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NeoAgi.CommandLine;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NLog.Extensions.Logging;
using NLog;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(builder =>
            {
                builder.Sources.Clear();

                builder.AddJsonFile("appsettings.json", optional: false);

                // builder.AddCommandLineOptions<PrunerConfig>(args);
            })
            .ConfigureLogging(logBuilder =>
            {
                logBuilder.ClearProviders();
                logBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                logBuilder.AddNLog("nlog.config.xml");
            })
        .ConfigureServices((hostContext, services) =>
            {
                services.Configure<PrunerConfig>(hostContext.Configuration.GetSection("AppSettings"));
                services.AddHostedService<Worker>();
            });
}