using System.Diagnostics;
using System.Text;
using System.Text.Json;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Processor;

public class InputDeserializer
{
    private readonly ActivitySource _source;

    public InputDeserializer(ActivitySource source)
    {
        _source = source;
    }
    
    public List<KinesisDataRecord<MessageWrapper<CustomerCreatedEvent>>> DeserializeFromEnvironment()
    {
        Console.WriteLine($"Environment data set to: {Environment.GetEnvironmentVariable("INPUT_MESSAGE")}");

        var messages = JsonSerializer.Deserialize<List<KinesisDataRecord<MessageWrapper<CustomerCreatedEvent>>>>(
            Environment.GetEnvironmentVariable("INPUT_MESSAGE"),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return messages;
    }
}