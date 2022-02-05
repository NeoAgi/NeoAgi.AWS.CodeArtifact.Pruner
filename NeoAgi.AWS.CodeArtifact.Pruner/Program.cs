// See https://aka.ms/new-console-template for more information

using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NeoAgi;
using NeoAgi.CommandLine.Exceptions;
using NLog.Extensions.Logging;
using NLog;
using System;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            CreateHostBuilder(args).Build().Run();
        }
        catch (CommandLineOptionParseException)
        {
            // Squelch the exception.  Output is captured below.
        }
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
                logBuilder.SetMinimumLevel(Enum<Microsoft.Extensions.Logging.LogLevel>.ParseOrDefault(
                    hostContext.Configuration.GetValue<string>("AppSettings:LogLevel"), Microsoft.Extensions.Logging.LogLevel.Debug));
                logBuilder.AddNLog("nlog.config.xml");
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<PrunerConfig>(hostContext.Configuration.GetSection("AppSettings"));
                services.AddHostedService<Worker>();
            });
}