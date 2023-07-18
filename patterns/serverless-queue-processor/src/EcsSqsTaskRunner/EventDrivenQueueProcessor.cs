using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.Pipes;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.StepFunctions;
using Constructs;
using AssetOptions = Amazon.CDK.AWS.S3.Assets.AssetOptions;
using Cluster = Amazon.CDK.AWS.ECS.Cluster;

namespace EcsSqsTaskRunner;

public record EventDrivenQueueProcessorProps(Cluster Cluster, string DockerFilePath, string CpuConfiguration, string MemoryConfiguration);

public class EventDrivenQueueProcessor
{
    private Construct _scope;
    private string _id;
    private EventDrivenQueueProcessorProps _props;
    
    public EventDrivenQueueProcessor(Construct scope, string id, EventDrivenQueueProcessorProps props)
    {
            this._scope = scope;
            this._id = id;
            this._props = props;
            
            var taskDef = BuildEcsTaskDefinition();

            var workflow = BuildWorkflow(taskDef);

            taskDef.GrantRun(workflow);

            BuildWorkflowSource(workflow);
        }

        private void BuildWorkflowSource(StateMachine workflow)
        {
            var queue = new Queue(this._scope, $"{this._id}-InboundMessageQueue", new QueueProps());

            var sourcePolicy = new PolicyDocument(
                new PolicyDocumentProps
                {
                    Statements = new[]
                    {
                        new PolicyStatement(
                            new PolicyStatementProps
                            {
                                Resources = new[] { queue.QueueArn },
                                Actions = new[] { "sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes" },
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
                this._scope,
                $"{this._id}-PipeRole",
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
                this._scope,
                $"{this._id}Pipe",
                new CfnPipeProps
                {
                    RoleArn = pipeRole.RoleArn,
                    Source = queue.QueueArn,
                    SourceParameters = new CfnPipe.PipeSourceParametersProperty
                    {
                        SqsQueueParameters = new CfnPipe.PipeSourceSqsQueueParametersProperty
                        {
                            BatchSize = 10,
                            MaximumBatchingWindowInSeconds = 5
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

        private StateMachine BuildWorkflow(TaskDefinition taskDef)
        {
            var logGroup = new LogGroup(this._scope, $"{this._id}-WorkflowLogGroup", new LogGroupProps()
            {
                Retention = RetentionDays.ONE_DAY,
                RemovalPolicy = RemovalPolicy.DESTROY,
                LogGroupName = $"{this._id}LogGroup"
            });
            
            var workflow = new StateMachine(this._scope, $"{this._id}TriggerWorkflow", new StateMachineProps
            {
                DefinitionBody = DefinitionBody.FromFile("./src/EcsSqsTaskRunner/statemachine/statemachine.asl.json",
                    new AssetOptions()),
                DefinitionSubstitutions = new Dictionary<string, string>(2)
                {
                    { "SUBNET_1", this._props.Cluster.Vpc.PublicSubnets[0].SubnetId },
                    { "SUBNET_2", this._props.Cluster.Vpc.PublicSubnets[0].SubnetId },
                    { "CLUSTER_NAME", this._props.Cluster.ClusterName },
                    { "TASK_DEFINITION", taskDef.TaskDefinitionArn }
                },
                StateMachineType = StateMachineType.EXPRESS,
                TracingEnabled = true,
                Logs = new LogOptions
                {
                    Destination = logGroup,
                    IncludeExecutionData = true,
                    Level = LogLevel.ALL,

                }
            });
            return workflow;
        }

        private TaskDefinition BuildEcsTaskDefinition()
        {
            var passRolePolicy = new Policy(this._scope, $"{this._id}-iam-pass-role", new PolicyProps()
            {
                Statements = new[]
                {
                    new PolicyStatement(new PolicyStatementProps()
                    {
                        Actions = new[] { "iam:PassRole" },
                        Effect = Effect.ALLOW,
                        Resources = new[] { "*" },
                        Sid = "AllowPassRole"
                    })
                }
            });

            var taskRole = new Role(this._scope, $"{this._id}-TaskRole", new RoleProps()
            {
                AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromManagedPolicyArn(this._scope, "TaskRoleManaged",
                        "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"),
                }
            });

            var taskExecutionRole = new Role(this._scope, $"{this._id}-TaskExecutionRole", new RoleProps()
            {
                AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromManagedPolicyArn(this._scope, "TaskExecutionRoleManaged",
                        "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"),
                }
            });

            taskRole.AttachInlinePolicy(passRolePolicy);

            var taskDef = new TaskDefinition(this._scope, $"{this._id}-task-definition", new TaskDefinitionProps()
            {
                Family = $"{this._id}-task-definition",
                RuntimePlatform = new RuntimePlatform()
                {
                    CpuArchitecture = CpuArchitecture.ARM64,
                    OperatingSystemFamily = OperatingSystemFamily.LINUX
                },
                NetworkMode = NetworkMode.AWS_VPC,
                Cpu = this._props.CpuConfiguration,
                MemoryMiB = this._props.MemoryConfiguration,
                Compatibility = Compatibility.FARGATE,
                TaskRole = taskRole,
                ExecutionRole = taskExecutionRole
            });

            var logGroup = new LogGroup(this._scope, $"{this._id}-ContainerLogGroup", new LogGroupProps()
            {
                Retention = RetentionDays.ONE_DAY,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            logGroup.GrantWrite(taskExecutionRole);

            taskDef.AddContainer($"{this._id}-Container", new ContainerDefinitionOptions()
            {
                Image = ContainerImage.FromAsset(this._props.DockerFilePath),
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps()
                {
                    LogGroup = logGroup,
                    StreamPrefix = this._id
                }),
            });
            return taskDef;
        }
}