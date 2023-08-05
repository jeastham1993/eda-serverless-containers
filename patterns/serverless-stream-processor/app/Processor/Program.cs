using System.Diagnostics;
using System.Text;
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

var ssmClient = new AmazonSimpleSystemsManagementClient();
var stepFunctionsClient = new AmazonStepFunctionsClient();

try
{
    var parameter = await ssmClient.GetParameterAsync(new GetParameterRequest()
    {
        Name = "honeycomb-api-key"
    });

    var applicationName = "com.StreamPublisher";

    ActivitySource publisherActivitySource = new(applicationName);

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

    Console.WriteLine($"Environment data set to: {Environment.GetEnvironmentVariable("INPUT_MESSAGE")}");

    var messages = JsonSerializer.Deserialize<List<KinesisDataRecord>>(
        Environment.GetEnvironmentVariable("INPUT_MESSAGE"),
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

    foreach (var message in messages)
    {
        var messageBytes = Convert.FromBase64String(message.Data);

        var messageBody = MemoryStreamToString(new MemoryStream(messageBytes));

        var data = JsonSerializer.Deserialize<KinesisDataMessage>(messageBody);

        Console.WriteLine($"TraceId: {data.TraceId}. SpanId: {data.SpanId}");
        
        using var messageProcessingActivity = publisherActivitySource.StartActivityWithLink(data);

        try
        {
            messageProcessingActivity.AddTag("message.partition", message.PartitionKey);
            messageProcessingActivity.AddTag("message.sequencenumber", message.SequenceNumber);

            await Task.Delay(TimeSpan.FromSeconds(5));

            if (data.Message == "force-failure")
            {
                throw new Exception("Failure processing message");
            }
        }
        catch (Exception e)
        {
            messageProcessingActivity.RecordException(e);
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
    await stepFunctionsClient.SendTaskFailureAsync(new SendTaskFailureRequest()
    {
        Error = "ProcessingException",
        TaskToken = Environment.GetEnvironmentVariable("TASK_TOKEN"),
        Cause = JsonSerializer.Serialize(new ProcessingResult(false, ex.Message))
    });
}

static string MemoryStreamToString(MemoryStream memoryStream)
{
    memoryStream.Position = 0; //Reset the memoryStream position to the beginning
    using var reader = new StreamReader(memoryStream, Encoding.UTF8); //Use the appropriate Encoding here
    return reader.ReadToEnd();
}

internal record KinesisDataMessage
{
    public string Message { get; set; }

    public string TraceId { get; set; }

    public string SpanId { get; set; }
}