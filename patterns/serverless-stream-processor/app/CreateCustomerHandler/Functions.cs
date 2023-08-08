using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace CreateCustomerHandler;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

using Honeycomb.OpenTelemetry;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// A collection of sample Lambda functions that provide a REST api for doing simple math calculations.
/// </summary>
public class Functions
{
    const string ApplicationName = "com.JamesEastham.StreamPublisher";
    private readonly ActivitySource publisherActivitySource;
    private readonly TracerProvider tracerProvider;
    private readonly AmazonKinesisClient client;

    public Functions()
    {
        publisherActivitySource = new(ApplicationName);
        
        var traceBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService((ApplicationName)))
            .AddSource(ApplicationName)
            .AddConsoleExporter();

        try
        {
            var ssmClient = new AmazonSimpleSystemsManagementClient();
            
            var parameter = ssmClient.GetParameterAsync(new GetParameterRequest()
            {
                Name = "honeycomb-api-key"
            }).GetAwaiter().GetResult();

            var options = new HoneycombOptions
            {
                ServiceName = "processor",
                ServiceVersion = "1.0.0",
                ApiKey = parameter.Parameter.Value,
                ResourceBuilder = ResourceBuilder.CreateDefault()
            };

            traceBuilder.AddHoneycomb(options);
        }
        catch (Exception)
        {
        }

        this.tracerProvider = traceBuilder
            .Build();
        
        client = new AmazonKinesisClient();
    }
    
    [LambdaFunction]
    [HttpApi(
        LambdaHttpMethod.Post,
        "/")]
    public async Task<CustomerCreatedEvent> CreateCustomer([FromBody] CreateCustomerCommand command)
    {
        using (var activity = publisherActivitySource.StartActivity("Processing messages"))
        {
            using var publishingActivity = publisherActivitySource.StartActivity("Publishing message");

            var customerEvent = new CustomerCreatedEvent
                { FirstName = command.FirstName, CustomerId = Guid.NewGuid().ToString() };

            publishingActivity.AddTag(
                "customer.id",
                customerEvent.CustomerId);

            var messageJson = JsonSerializer.Serialize(
                new MessageWrapper<CustomerCreatedEvent>(customerEvent));

            await client.PutRecordAsync(
                new PutRecordRequest
                {
                    Data = new MemoryStream(Encoding.UTF8.GetBytes(messageJson)),
                    StreamName = Environment.GetEnvironmentVariable("STREAM_NAME"),
                    PartitionKey = "default"
                });

            return customerEvent;
        }
    }
}