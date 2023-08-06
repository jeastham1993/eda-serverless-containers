using System.Diagnostics;
using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Processor;

await Startup.Init();

var stepFunctionsClient = Startup.Services.GetRequiredService<AmazonStepFunctionsClient>();
var deserializer = Startup.Services.GetRequiredService<InputDeserializer>();
var activitySource = Startup.Services.GetRequiredService<ActivitySource>();
var traceProvider = Startup.Services.GetRequiredService<TracerProvider>();
var logger = Startup.Services.GetRequiredService<ILogger>();

try
{
    logger.LogInformation("Processing messages");
    
    var kinesisMessages = deserializer.DeserializeFromEnvironment();

    logger.LogInformation($"Found {kinesisMessages.Count} message(s) to process");

    using var activity = activitySource.StartActivity("Consuming messages");

    activity.AddTag("messages.count", kinesisMessages.Count);

    foreach (var message in kinesisMessages)
    {
        try
        {
            logger.LogInformation($"Processing {message.SequenceNumber}");
            
            var context = message.Data.Metadata.LoadActivityContext();

            using var messageActivity = activitySource.StartActivity("Consume message", ActivityKind.Consumer,
                default(ActivityContext), links: new ActivityLink[1] { new ActivityLink(context) });
            
            Activity.Current.AddTag("stream.partition", message.PartitionKey);
            Activity.Current.AddTag("stream.sequencenumber", message.SequenceNumber);
            
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (message.Data.Data.FirstName == "force-failure")
            {
                throw new Exception("Failure processing message");
            }
        }
        catch (Exception e)
        {
            logger.LogError("Failure", e);
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
catch (Exception ex)
{
    logger.LogError("Catastrophic failure", ex);
    
    await stepFunctionsClient.SendTaskFailureAsync(new SendTaskFailureRequest()
    {
        Error = "ProcessingException",
        TaskToken = Environment.GetEnvironmentVariable("TASK_TOKEN"),
        Cause = JsonSerializer.Serialize(new ProcessingResult(false, ex.Message))
    });
}