using Amazon.SQS;
using Amazon.SQS.Model;

namespace QueueWorker;

public class WorkerWithPoller : BackgroundService
{
    private readonly ILogger<WorkerWithPoller> _logger;
    private readonly AmazonSQSClient _amazonSqsClient;

    public WorkerWithPoller(ILogger<WorkerWithPoller> logger, AmazonSQSClient amazonSqsClient)
    {
        _logger = logger;
        _amazonSqsClient = amazonSqsClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var messages =
                await this._amazonSqsClient.ReceiveMessageAsync(Environment.GetEnvironmentVariable("QUEUE_URL"));

            var toDelete = new List<DeleteMessageBatchRequestEntry>();

            foreach (var message in messages.Messages)
            {
                try
                {
                    // Do Work, thread simulates work being done
                    this._logger.LogInformation($"Processing message {message.MessageId} with body: '{message.Body}'");

                    await Task.Delay(TimeSpan.FromSeconds(5));

                    this._logger.LogInformation($"Processing of message {message.MessageId} complete");
                
                    toDelete.Add(new DeleteMessageBatchRequestEntry(message.MessageId, message.ReceiptHandle));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            await this._amazonSqsClient.DeleteMessageBatchAsync(Environment.GetEnvironmentVariable("QUEUE_URL"),
                toDelete);

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}