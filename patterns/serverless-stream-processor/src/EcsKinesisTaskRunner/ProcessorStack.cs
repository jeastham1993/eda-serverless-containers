using System.Collections.Generic;
using System.IO;
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Kinesis;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.Pipes;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.StepFunctions;
using Constructs;
using AssetOptions = Amazon.CDK.AWS.S3.Assets.AssetOptions;
using Stream = Amazon.CDK.AWS.Kinesis.Stream;


namespace EcsKinesisTaskRunner;

using Amazon.CDK.AWS.SQS;

public record ProcessorStackProps(Stream DataStream);

public class ProcessorStack : Stack
{
    internal ProcessorStack(Construct scope, string id, ProcessorStackProps processorProps, IStackProps props = null) : base(scope, id, props)
    {
        var vpc = new Vpc(this, "ecs-kinesis-cluster-network", new VpcProps
        {
            EnableDnsHostnames = true,
            EnableDnsSupport = true,
            IpAddresses = IpAddresses.Cidr("10.30.0.0/16"),
            MaxAzs = 2,
            NatGateways = 0,
            SubnetConfiguration = new ISubnetConfiguration[]
            {
                new SubnetConfiguration
                {
                    Name = "Public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 24
                },
                new SubnetConfiguration
                {
                    Name = "Private",
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    CidrMask = 24
                }
            }
        });

        var applicationSecurityGroup = new SecurityGroup(this, "ApplicationSecurityGroup", new SecurityGroupProps
        {
            Vpc = vpc,
            AllowAllIpv6Outbound = true,
            AllowAllOutbound = true,
            Description = "Security group for cluster tasks to run in."
        });

        var cluster = new Cluster(this, "ecs-kinesis-cluster", new ClusterProps
        {
            ContainerInsights = true,
            Vpc = vpc
        });
        
        var bucket = new Bucket(this, "RecordStorageBucket", new BucketProps()
        {
            BucketName = $"{this.Account}-temporary-storage-bucket"
        });

        var storageOutput = new Table(this, "StorageTable", new TableProps()
        {
            PartitionKey = new Attribute()
            {
                Name = "PK",
                Type = AttributeType.STRING
            },
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });

        var taskDef = BuildEcsTaskDefinition(bucket, storageOutput);

        var workflow = BuildWorkflow(cluster, applicationSecurityGroup, taskDef, bucket);

        taskDef.GrantRun(workflow);

        BuildWorkflowSource(workflow, processorProps.DataStream);
    }
    
    private void BuildWorkflowSource(StateMachine workflow, Stream dataStream)
    {
        var sourcePolicy = new PolicyDocument(
            new PolicyDocumentProps
            {
                Statements = new[]
                {
                    new PolicyStatement(
                        new PolicyStatementProps
                        {
                            Resources = new[] { dataStream.StreamArn },
                            Actions = new[]
                            {
                                "kinesis:DescribeStream",
                                "kinesis:DescribeStreamSummary",
                                "kinesis:GetRecords",
                                "kinesis:GetShardIterator",
                                "kinesis:ListStreams",
                                "kinesis:ListShards"
                            },
                            Effect = Effect.ALLOW
                        })
                }
            });

        var targetPolicy = new PolicyDocument(
            new PolicyDocumentProps
            {
                Statements = new[]
                {
                    new PolicyStatement(
                        new PolicyStatementProps
                        {
                            Resources = new[] { workflow.StateMachineArn },
                            Actions = new[] { "states:StartExecution" },
                            Effect = Effect.ALLOW
                        })
                }
            });

        var pipeRole = new Role(
            this,
            "PipeRole",
            new RoleProps
            {
                AssumedBy = new ServicePrincipal("pipes.amazonaws.com"),
                InlinePolicies = new Dictionary<string, PolicyDocument>(2)
                {
                    { "SourcePolicy", sourcePolicy },
                    { "TargetPolicy", targetPolicy }
                }
            });

        var pipe = new CfnPipe(
            this,
            "KinesisPipe",
            new CfnPipeProps
            {
                RoleArn = pipeRole.RoleArn,
                Source = dataStream.StreamArn,
                SourceParameters = new CfnPipe.PipeSourceParametersProperty
                {
                    KinesisStreamParameters = new CfnPipe.PipeSourceKinesisStreamParametersProperty
                    {
                        StartingPosition = "LATEST",
                        BatchSize = 3,
                        MaximumBatchingWindowInSeconds = 10
                    }
                },
                Target = workflow.StateMachineArn,
                TargetParameters = new CfnPipe.PipeTargetParametersProperty
                {
                    InputTemplate = File.ReadAllText("./src/EcsKinesisTaskRunner/transformers/kinesis-input-transformer.json"),
                    StepFunctionStateMachineParameters = new CfnPipe.PipeTargetStateMachineParametersProperty
                    {
                        InvocationType = "FIRE_AND_FORGET"
                    }
                }
            });
    }

    private StateMachine BuildWorkflow(Cluster cluster, SecurityGroup securityGroup, TaskDefinition taskDef, Bucket bucket)
    {
        var failedMessageQueue = new Queue(this, "FailedMessageQueue", new QueueProps());
        
        var logGroup = new LogGroup(this, "WorkflowLogGroup", new LogGroupProps
        {
            Retention = RetentionDays.ONE_DAY,
            RemovalPolicy = RemovalPolicy.DESTROY,
            LogGroupName = "EcsTriggerWorkflowLogGroup"
        });

        var workflow = new StateMachine(this, "EcsTriggerStateMachine", new StateMachineProps
        {
            DefinitionBody = DefinitionBody.FromFile("./src/EcsKinesisTaskRunner/statemachine/statemachine.asl.json",
                new AssetOptions()),
            DefinitionSubstitutions = new Dictionary<string, string>(2)
            {
                { "SUBNET_1", cluster.Vpc.PublicSubnets[0].SubnetId },
                { "SUBNET_2", cluster.Vpc.PublicSubnets[0].SubnetId },
                { "SECURITY_GROUP_ID", securityGroup.SecurityGroupId },
                { "CLUSTER_NAME", cluster.ClusterName },
                { "TASK_DEFINITION", taskDef.TaskDefinitionArn },
                { "QUEUE_URL", failedMessageQueue.QueueUrl },
                { "BUCKET_NAME", bucket.BucketName },
            },
            StateMachineType = StateMachineType.STANDARD,
            TracingEnabled = true,
            Logs = new LogOptions
            {
                Destination = logGroup,
                IncludeExecutionData = true,
                Level = LogLevel.ALL
            }
        });

        bucket.GrantWrite(workflow);
        failedMessageQueue.GrantSendMessages(workflow);
        
        return workflow;
    }

    private TaskDefinition BuildEcsTaskDefinition(Bucket bucket, Table output)
    {
        var passRolePolicy = new Policy(this, "iam-pass-role", new PolicyProps
        {
            Statements = new[]
            {
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = new[] { "iam:PassRole" },
                    Effect = Effect.ALLOW,
                    Resources = new[] { "*" },
                    Sid = "AllowPassRole"
                })
            }
        });
        var ssmReadOnly = new Policy(this, "app-permissions", new PolicyProps
        {
            Statements = new[]
            {
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = new[] { "ssm:GetParameter" },
                    Effect = Effect.ALLOW,
                    Resources = new[] { "*" },
                    Sid = "SSMReadOnly"
                }),
                
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = new[] { "states:SendTaskSuccess", "states:SendTaskFailure" },
                    Effect = Effect.ALLOW,
                    Resources = new[] { "*" },
                    Sid = "StepFunctionsCallbackPermissions"
                })
            }
        });

        var taskRole = new Role(this, "TaskRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromManagedPolicyArn(this, "TaskRoleManaged",
                    "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        var taskExecutionRole = new Role(this, "TaskExecutionRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromManagedPolicyArn(this, "TaskExecutionRoleManaged",
                    "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        taskRole.AttachInlinePolicy(passRolePolicy);
        taskRole.AttachInlinePolicy(ssmReadOnly);
        bucket.GrantRead(taskRole);
        output.GrantWriteData(taskRole);

        var taskDef = new TaskDefinition(this, "task-def", new TaskDefinitionProps
        {
            Family = "dotnet-poller-task-definition",
            RuntimePlatform = new RuntimePlatform
            {
                CpuArchitecture = CpuArchitecture.ARM64,
                OperatingSystemFamily = OperatingSystemFamily.LINUX
            },
            NetworkMode = NetworkMode.AWS_VPC,
            Cpu = "1024",
            MemoryMiB = "2048",
            Compatibility = Compatibility.FARGATE,
            TaskRole = taskRole,
            ExecutionRole = taskExecutionRole
        });

        var logGroup = new LogGroup(this, "ContainerLogGroup", new LogGroupProps
        {
            Retention = RetentionDays.ONE_DAY,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        logGroup.GrantWrite(taskExecutionRole);

        taskDef.AddContainer("SampleContainer", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromAsset("./app/Processor/"),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "Processor"
            }),
            Environment = new Dictionary<string, string>(2)
            {
                {"BUCKET_NAME", bucket.BucketName},
                {"TABLE_NAME", output.TableName}
            }
        });
        return taskDef;
    }
}