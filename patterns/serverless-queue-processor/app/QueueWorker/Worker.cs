using System.Text.Json;
using Amazon.SQS.Model;

namespace QueueWorker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public Worker(ILogger<Worker> logger, IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation($"Environment data set to: {Environment.GetEnvironmentVariable("INPUT_MESSAGE")}");

        var messages = JsonSerializer.Deserialize<List<Message>>(Environment.GetEnvironmentVariable("INPUT_MESSAGE"), new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        });

        foreach (var message in messages)
        {
            // Do Work, thread simulates work being done
            this._logger.LogInformation($"Processing message {message.MessageId} with body: '{message.Body}'");

            await Task.Delay(TimeSpan.FromSeconds(5));

            this._logger.LogInformation($"Processing of message {message.MessageId} complete");
        }
        
        this._hostApplicationLifetime.StopApplication();
    }
}