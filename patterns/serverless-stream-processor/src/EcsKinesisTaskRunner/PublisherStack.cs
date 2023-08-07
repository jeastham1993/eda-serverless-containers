namespace EcsKinesisTaskRunner;

using System.Collections.Generic;

using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Kinesis;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using BundlingOptions = Amazon.CDK.BundlingOptions;

using Constructs;

public class PublisherStack : Stack
{
    public Stream DataStream { get; private set; }
    
    internal PublisherStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        this.DataStream = new Stream(this, "MessageStream", new StreamProps
        {
            StreamMode = StreamMode.ON_DEMAND,
            RetentionPeriod = Duration.Days(1)
        });
        
        this.ConfigurePublisher();
    }
    
    private void ConfigurePublisher()
    {
        var buildOption = new BundlingOptions()
        {
            Image = Runtime.DOTNET_6.BundlingImage,
            User = "root",
            OutputType = BundlingOutput.ARCHIVED,
            Command = new string[]{
                "/bin/sh",
                "-c",
                " dotnet tool install -g Amazon.Lambda.Tools"+
                " && dotnet build"+
                " && dotnet lambda package --output-package /asset-output/function.zip"
            }
        };
        
        var functionCustomPolicy = new Policy(this, "function-permissions", new PolicyProps
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
            }
        });
        
        var functionRole = new Role(
            this,
            "FunctionRole",
            new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            });
        
        functionRole.AttachInlinePolicy(functionCustomPolicy);
        functionRole.AddManagedPolicy(ManagedPolicy.FromManagedPolicyArn(this, "LambdaExecution", "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"));
        this.DataStream.GrantWrite(functionRole);
        
        var function = new Function(
            this,
            "ApiHandler",
            new FunctionProps()
            {
                Handler =
                    "CreateCustomerHandler::CreateCustomerHandler.Functions_CreateCustomer_Generated::CreateCustomer",
                FunctionName = "CreateCustomerHandler",
                Runtime = Runtime.DOTNET_6,
                MemorySize = 1024,
                LogRetention = RetentionDays.ONE_DAY,
                Environment = new Dictionary<string, string>(1)
                {
                    {"STREAM_NAME", this.DataStream.StreamName}
                },
                Role = functionRole,
                Tracing = Tracing.ACTIVE,
                Code = Code.FromAsset(".\\app\\CreateCustomerHandler\\", new Amazon.CDK.AWS.S3.Assets.AssetOptions
                {
                    Bundling = buildOption
                }),
                Architecture = Architecture.X86_64
            });
        
        var api = new RestApi(
            this,
            $"CustomerApi",
            new RestApiProps()
            {
                RestApiName = $"CustomerApi"
            });
        
        api.Root.AddMethod(
            "POST",
            new LambdaIntegration(function));

        var output = new CfnOutput(
            this,
            "ApiEndpoint",
            new CfnOutputProps()
            {
                ExportName = "CustomerApiUrl",
                Value = api.Url
            });
    }
}