// See https://aka.ms/new-console-template for more information

using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NeoAgi.CommandLine.Exceptions;
using NLog.Extensions.Logging;
using NLog;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            CreateHostBuilder(args).Build().Run();
        }
        catch (CommandLineOptionParseException ex)
        {
            foreach(var option in ex.OptionsWithErrors)
            {
                Console.WriteLine($"{option.Option.FriendlyName} - {option.Reason.ToString()}");
            }

            // Console.WriteLine(ex.ToString());
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