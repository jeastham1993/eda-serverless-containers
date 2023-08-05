// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Honeycomb.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var apiKey = new Option<string>(name: "--apikey", description: "Your Honeycomb API Key");
var message = new Option<string>(name: "--message", description: "The message to publish");

var rootCommand = new RootCommand("Kinesis Data Stream Publisher");
rootCommand.AddOption(apiKey);
rootCommand.AddOption(message);

rootCommand.SetHandler(async (apiKey, message) =>
{
    await PublishMessage(apiKey, message);
}, apiKey, message);

await rootCommand.InvokeAsync(args);

static async Task PublishMessage(string apiKey, string message)
{
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new ArgumentException("Honeycomb API cannot be null", nameof(apiKey));
    }
    
    var applicationName = "com.JamesEastham.StreamPublisher";

    ActivitySource publisherActivitySource = new(applicationName);

    var options = new HoneycombOptions
    {
        ServiceName = "publisher",
        ServiceVersion = "1.0.0",
        ApiKey = apiKey,
        ResourceBuilder = ResourceBuilder.CreateDefault()
    };

    using var tracerProvider = Sdk.CreateTracerProviderBuilder()
        .AddSource(applicationName)
        .AddConsoleExporter()
        .AddHoneycomb(options)
        .Build();

    var chain = new CredentialProfileStoreChain();
    AWSCredentials awsCredentials;
    if (chain.TryGetAWSCredentials("dev", out awsCredentials))
    {
        // Use awsCredentials to create an Amazon S3 service client
        using (var client = new AmazonKinesisClient(awsCredentials, RegionEndpoint.USEast1))
        {
            while (string.IsNullOrEmpty(message))
            {
                Console.WriteLine("What message would you like to send?");

                message = Console.ReadLine();    
            }

            using (var activity = publisherActivitySource.StartActivity("Publishing Message"))
            {
                var messageJson = JsonSerializer.Serialize(new KinesisDataMessage(message, activity.TraceId.ToString(), activity.SpanId.ToString()));

                var pushResponse = await client.PutRecordAsync(new PutRecordRequest
                {
                    Data = new MemoryStream(Encoding.UTF8.GetBytes(messageJson)),
                    StreamName = "EcsKinesisTaskRunner-MessageStreamA82FF37E-ClDSgmqCtxZc",
                    PartitionKey = "default"
                });
        
                Console.WriteLine($"Push status: {pushResponse.HttpStatusCode}");
            }
        }
    }
}

record KinesisDataMessage(string Message, string TraceId, string SpanId);