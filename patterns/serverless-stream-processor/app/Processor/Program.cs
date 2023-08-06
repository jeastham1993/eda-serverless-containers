using System.Diagnostics;
using System.Text.Json;
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

var parameter = await ssmClient.GetParameterAsync(new GetParameterRequest()
{
    Name = "honeycomb-api-key"
});

ActivitySource activitySource = new(applicationName);

var options = new HoneycombOptions
{
    ServiceName = "processor",
    ServiceVersion = "1.0.0",
    ApiKey = parameter.Parameter.Value,
    ResourceBuilder = ResourceBuilder.CreateDefault()
};

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(applicationName)
    .AddConsoleExporter()
    .AddHoneycomb(options)
    .Build();

var stepFunctionsClient = new AmazonStepFunctionsClient();
var deserializer = new InputDeserializer(activitySource);

try
{
    Console.WriteLine("Processing messages");
    
    var kinesisMessages = deserializer.DeserializeFromEnvironment();

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