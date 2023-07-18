using Amazon.SQS;
using QueueWorker;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<AmazonSQSClient>(new AmazonSQSClient());
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();