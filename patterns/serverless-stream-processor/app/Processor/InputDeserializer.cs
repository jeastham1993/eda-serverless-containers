using System.Diagnostics;
using System.Text.Json;
using Amazon.S3;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Processor;

public class InputDeserializer
{
    private readonly ActivitySource _source;
    private readonly AmazonS3Client _s3Client;

    public InputDeserializer(ActivitySource source, AmazonS3Client s3Client)
    {
        _source = source;
        _s3Client = s3Client;
    }
    
    public async Task<List<KinesisDataRecord<MessageWrapper<CustomerCreatedEvent>>>> DeserializeFromInput()
    {
        if (!string.IsNullOrEmpty("BUCKET_KEY"))
        {
            return await FromS3();
        }
        
        Console.WriteLine($"Environment data set to: {Environment.GetEnvironmentVariable("INPUT_MESSAGE")}");

        var messages = JsonSerializer.Deserialize<List<KinesisDataRecord<MessageWrapper<CustomerCreatedEvent>>>>(
            Environment.GetEnvironmentVariable("INPUT_MESSAGE"),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return messages;
    }
    
    private async Task<List<KinesisDataRecord<MessageWrapper<CustomerCreatedEvent>>>> FromS3()
    {
        var s3Object = await this._s3Client.GetObjectAsync(Environment.GetEnvironmentVariable("BUCKET_NAME"),
            Environment.GetEnvironmentVariable("BUCKET_KEY"));

        using (var streamReader = new StreamReader(s3Object.ResponseStream))
        {
            var objectData = await streamReader.ReadToEndAsync();
            
            var messages = JsonSerializer.Deserialize<List<KinesisDataRecord<MessageWrapper<CustomerCreatedEvent>>>>(
                objectData,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            
            return messages;
        }
    }
}