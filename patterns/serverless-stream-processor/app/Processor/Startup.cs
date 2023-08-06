using System.Diagnostics;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.StepFunctions;
using Honeycomb.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Processor;

public static class Startup
{
    public static IServiceProvider Services { get; set; }

    static Startup()
    {
        if (Services == null)
        {
            Init().GetAwaiter().GetResult();
        }
    }

    internal static async Task Init()
    {
        if (Services != null)
        {
            return;
        }

        Console.WriteLine("Running startup code");
        
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLogging();
        
        var ssmClient = new AmazonSimpleSystemsManagementClient();

        var parameter = await ssmClient.GetParameterAsync(new GetParameterRequest()
        {
            Name = "honeycomb-api-key"
        });
        
        Console.WriteLine("Retrieved API key");

        var applicationName = "com.StreamPublisher";

        ActivitySource publisherActivitySource = new(applicationName);

        var options = new HoneycombOptions
        {
            ServiceName = "processor",
            ServiceVersion = "1.0.0",
            ApiKey = parameter.Parameter.Value,
            ResourceBuilder = ResourceBuilder.CreateDefault()
        };

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(applicationName)
            .AddHoneycomb(options)
            .Build();
        
        Console.WriteLine("Configured tracer");

        serviceCollection.AddSingleton(publisherActivitySource);
        serviceCollection.AddSingleton(new AmazonStepFunctionsClient());
        serviceCollection.AddSingleton<InputDeserializer>();
        serviceCollection.AddSingleton(tracerProvider);
        
        Console.WriteLine("Building service provider");

        Services = serviceCollection.BuildServiceProvider();
    }
}