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
using StreamPublisher;

var apiKey = new Option<string>(name: "--apikey", description: "Your Honeycomb API Key");
var message = new Option<string>(name: "--message", description: "The message to publish");
var streamName = new Option<string>(name: "--stream-name", description: "The name of the stream to publish to");
var messagesToPublish = new Option<int>(name: "--number-to-publish", description: "The number of messages to publish");

var rootCommand = new RootCommand("Kinesis Data Stream Publisher");
rootCommand.AddOption(apiKey);
rootCommand.AddOption(message);
rootCommand.AddOption(streamName);
rootCommand.Add(messagesToPublish);

rootCommand.SetHandler(async (apiKey, message, streamName, messagesToPublish) =>
{
    await PublishMessage(apiKey, message, streamName, messagesToPublish);
}, apiKey, message, streamName, messagesToPublish);

await rootCommand.InvokeAsync(args);

static async Task PublishMessage(string apiKey, string message, string streamName, int messagesToPublish)
{
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new ArgumentException("Honeycomb API cannot be null", nameof(apiKey));
    }

    if (string.IsNullOrEmpty(streamName))
    {
        throw new ArgumentException("Stream name cannot be null");
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

            using (var activity = publisherActivitySource.StartActivity("Processing messages"))
            {
                for (var counter = 1; counter <= messagesToPublish; counter++)
                {
                    using var publishingActivity = publisherActivitySource.StartActivity("Publishing message");

                    publishingActivity.AddTag("customer.id", $"customer-{counter}");
                    
                    var messageJson = JsonSerializer.Serialize(new MessageWrapper<CustomerCreatedEvent>(
                        new CustomerCreatedEvent() { FirstName = message, CustomerId = $"customer-{counter}" }));

                    var pushResponse = await client.PutRecordAsync(new PutRecordRequest
                    {
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(messageJson)),
                        StreamName = streamName,
                        PartitionKey = "default"
                    });
        
                    Console.WriteLine($"Push status: {pushResponse.HttpStatusCode}");   
                }
            }
        }
    }
}