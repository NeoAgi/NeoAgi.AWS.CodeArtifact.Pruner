// See https://aka.ms/new-console-template for more information

using Amazon.CodeArtifact;
using Amazon.CodeArtifact.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeoAgi.AWS.CodeArtifact.Pruner;
using NeoAgi.Runtime.Serialization.Json;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<Worker>();
            });
}