using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Lambda;
using AssetOptions = Amazon.CDK.AWS.S3.Assets.AssetOptions;
using Constructs;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.Events.Targets;

namespace Infrastructure
{
    using XaasKit.CDK.AWS.Lambda.DotNet;

    using BundlingOptions = Amazon.CDK.BundlingOptions;

    public class LambdaProxyProps
    {
        public string ProxyIdentifier { get; set; }

        public string ForwardingUrl { get; set; }

        public Amazon.CDK.AWS.Events.IEventBus EventBus { get; set; }

        public EventPattern EventPattern { get; set; }

        public IVpc Vpc { get; set; }

        public ISubnetSelection Subnets { get; set; }
    }

    public class LambdaProxy : Construct
    {
        public LambdaProxy(Construct scope, string id, LambdaProxyProps props) : base(scope, id)
        {
            var deadLetterQueue = new Queue(this, $"{props.ProxyIdentifier}-dlq", new QueueProps
            {
                QueueName = $"{props.ProxyIdentifier}-dlq",
                VisibilityTimeout = Duration.Minutes(1)
            });

            var queue = new Queue(this, $"{props.ProxyIdentifier}-queue", new QueueProps
            {
                QueueName = $"{props.ProxyIdentifier}-queue",
                VisibilityTimeout = Duration.Minutes(1),
                DeadLetterQueue = new DeadLetterQueue()
                {
                    Queue = deadLetterQueue,
                    MaxReceiveCount = 2
                },
            });

            var function = new DotNetFunction(this,
                "proxy-function",
                new DotNetFunctionProps()
                {
                    FunctionName = $"{props.ProxyIdentifier}-proxy-function",
                    Runtime = Runtime.DOTNET_6,
                    Environment = new Dictionary<string, string>
                    {
                        {"FORWARDING_URL", props.ForwardingUrl},
                        {"DEAD_LETTER_QUEUE_URL", deadLetterQueue.QueueUrl},
                    },
                    Vpc = props.Vpc,
                    VpcSubnets = props.Subnets,
                    Timeout = Duration.Seconds(10),
                    ProjectDir = "./src/Infrastructure/Constructs/LambdaProxyHandler",
                    Handler = "LambdaProxyHandler::LambdaProxyHandler.Function::FunctionHandler",
                });

            function.AddEventSource(new SqsEventSource(queue, new SqsEventSourceProps()
            {
                Enabled = true,
                ReportBatchItemFailures = true
            }));

            queue.GrantConsumeMessages(function);
            deadLetterQueue.GrantSendMessages(function);

            var rule = new Rule(this, $"{props.ProxyIdentifier}-rule", new RuleProps()
            {
                EventBus = props.EventBus,
                EventPattern = props.EventPattern,
                RuleName = $"{props.ProxyIdentifier}-rule",
                Targets = new[]
                {
                    new SqsQueue(queue)
                }
            });
        }
    }
}
