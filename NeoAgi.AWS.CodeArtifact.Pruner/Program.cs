// See https://aka.ms/new-console-template for more information

using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NeoAgi.AWS.CodeArtifact.Pruner.Models;
using NeoAgi.CommandLine.Exceptions;
using NLog;
using NLog.Extensions.Logging;
using NLog.Layouts;
using System;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            CreateHostBuilder(args).Build().Run();
        }
        catch (RaiseHelpException) { }               // Supporess RaiseHelpException as a NOOP
        catch (CommandLineOptionParseException) { }  // Suppress to use the default formatter
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.Sources.Clear();
                configuration.AddJsonFile("appsettings.json", optional: false);
                configuration.AddOpts<PrunerConfig>(args, "AppSettings", outputStream: Console.Out);
            })
            .ConfigureLogging((hostContext, logBuilder) =>
            {
                logBuilder.ClearProviders();

                Microsoft.Extensions.Logging.LogLevel minimumLogLevel = Enum<Microsoft.Extensions.Logging.LogLevel>.ParseOrDefault(
                    hostContext.Configuration.GetValue<string>("AppSettings:LogLevel"), Microsoft.Extensions.Logging.LogLevel.Information);

                logBuilder.SetMinimumLevel(minimumLogLevel);

                var config = new NLog.Config.LoggingConfiguration();

                // Targets where to log to: File and Console
                var logconsole = new NLog.Targets.ColoredConsoleTarget("logconsole");
                logconsole.Layout = NLogHelper.Layout;

                // Rules for mapping loggers to targets
                config.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, logconsole);
                config.AddRule(NLogHelper.MapLogLevel(minimumLogLevel), NLog.LogLevel.Fatal, logconsole, "NeoAgi.*");

                // Apply config           
                NLog.LogManager.Configuration = config;

                // Finally add NLog to the Configuration Builder
                logBuilder.AddNLog();
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Configure the Amazon Clients
                var codeArtifactClient = new AmazonCodeArtifactClient(new AmazonCodeArtifactConfig()
                {
                    RegionEndpoint = Amazon.RegionEndpoint.USWest2
                });

                services.AddSingleton<AmazonCodeArtifactClient>(codeArtifactClient);
                services.Configure<PrunerConfig>(hostContext.Configuration.GetSection("AppSettings"));
                services.AddHostedService<Worker>();
            });
}