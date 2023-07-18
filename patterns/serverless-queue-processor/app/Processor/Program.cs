using System.Text.Json;
using Amazon.SQS.Model;

Console.WriteLine($"Environment data set to: {Environment.GetEnvironmentVariable("INPUT_MESSAGE")}");

var messages = JsonSerializer.Deserialize<List<Message>>(Environment.GetEnvironmentVariable("INPUT_MESSAGE"), new JsonSerializerOptions()
{
    PropertyNameCaseInsensitive = true
});

foreach (var message in messages)
{
    // Do Work, thread simulates work being done
    Console.WriteLine($"Processing message {message.MessageId} with body: '{message.Body}'");

    await Task.Delay(TimeSpan.FromSeconds(5));

    Console.WriteLine($"Processing of message {message.MessageId} complete");
}