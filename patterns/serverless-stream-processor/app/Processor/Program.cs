using System.Diagnostics;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Honeycomb.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Processor;

var applicationName = "com.StreamPublisher";
        
var ssmClient = new AmazonSimpleSystemsManagementClient();

var traceBuilder = Sdk.CreateTracerProviderBuilder()
    .AddSource(applicationName)
    .AddConsoleExporter();

try
{
    var parameter = await ssmClient.GetParameterAsync(new GetParameterRequest()
    {
        Name = "honeycomb-api-key"
    });

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

ActivitySource activitySource = new(applicationName);

using var tracerProvider = traceBuilder
    .Build();

var stepFunctionsClient = new AmazonStepFunctionsClient();
var s3Client = new AmazonS3Client();
var dynamoClient = new AmazonDynamoDBClient();
var deserializer = new InputDeserializer(activitySource, s3Client);

try
{
    Console.WriteLine("Processing messages");
    
    var kinesisMessages = await deserializer.DeserializeFromInput();

    using (var instanceActivity = activitySource.StartActivity("Processing messages"))
    {
        instanceActivity?.AddTag("messages.count", kinesisMessages.Count);
        
        Console.WriteLine($"Found {kinesisMessages.Count} message(s) to process");

        foreach (var message in kinesisMessages)
        {
            try
            {
                Console.WriteLine($"Processing {message.SequenceNumber}");

                using (var messageActivity = activitySource.StartActivityWithLink(message.Data.Metadata))
                {
                    messageActivity?.AddTag("stream.partition", message.PartitionKey);
                    messageActivity?.AddTag("stream.sequencenumber", message.SequenceNumber);

                    await Task.Delay(TimeSpan.FromSeconds(5));

                    if (message.Data.Data.FirstName == "force-failure")
                    {
                        throw new Exception("Failure processing message");
                    }

                    await dynamoClient.PutItemAsync(Environment.GetEnvironmentVariable("TABLE_NAME"),
                        new Dictionary<string, AttributeValue>()
                        {
                            { "PK", new AttributeValue(message.Data.Data.CustomerId) },
                            { "FirstName", new AttributeValue(message.Data.Data.FirstName) }
                        });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failure - {e.Message}");
                Activity.Current.RecordException(e);
                throw;
            }
        }

        await stepFunctionsClient.SendTaskSuccessAsync(new SendTaskSuccessRequest()
        {
            Output = JsonSerializer.Serialize(new ProcessingResult(true, "OK")),
            TaskToken = Environment.GetEnvironmentVariable("TASK_TOKEN")
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Catastrophic failure - {ex.Message}");
    
    await stepFunctionsClient.SendTaskFailureAsync(new SendTaskFailureRequest()
    {
        Error = "ProcessingException",
        TaskToken = Environment.GetEnvironmentVariable("TASK_TOKEN"),
        Cause = JsonSerializer.Serialize(new ProcessingResult(false, ex.Message))
    });
}