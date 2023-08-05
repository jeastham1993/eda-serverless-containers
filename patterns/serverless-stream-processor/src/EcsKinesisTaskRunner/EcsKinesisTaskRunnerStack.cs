using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Kinesis;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.Pipes;
using Amazon.CDK.AWS.StepFunctions;
using Constructs;
using AssetOptions = Amazon.CDK.AWS.S3.Assets.AssetOptions;

namespace EcsKinesisTaskRunner;

public class EcsKinesisTaskRunnerStack : Stack
{
    internal EcsKinesisTaskRunnerStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
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

        var taskDef = BuildEcsTaskDefinition();

        var workflow = BuildWorkflow(cluster, applicationSecurityGroup, taskDef);

        taskDef.GrantRun(workflow);

        BuildWorkflowSource(workflow, taskDef, vpc, applicationSecurityGroup, cluster);
    }

    private void BuildWorkflowSource(StateMachine workflow, TaskDefinition taskDef, Vpc vpc,
        SecurityGroup securityGroup, Cluster cluster)
    {
        var dataStream = new Stream(this, "MessageStream", new StreamProps
        {
            StreamMode = StreamMode.ON_DEMAND,
            RetentionPeriod = Duration.Days(1)
        });

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
                        BatchSize = 10,
                        MaximumBatchingWindowInSeconds = 30
                    }
                },
                Target = workflow.StateMachineArn,
                TargetParameters = new CfnPipe.PipeTargetParametersProperty
                {
                    StepFunctionStateMachineParameters = new CfnPipe.PipeTargetStateMachineParametersProperty
                    {
                        InvocationType = "FIRE_AND_FORGET"
                    }
                }
            });
    }

    private StateMachine BuildWorkflow(Cluster cluster, SecurityGroup securityGroup, TaskDefinition taskDef)
    {
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
                { "TASK_DEFINITION", taskDef.TaskDefinitionArn }
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
        return workflow;
    }

    private TaskDefinition BuildEcsTaskDefinition()
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
            })
        });
        return taskDef;
    }
}