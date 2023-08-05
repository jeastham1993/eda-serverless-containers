using Amazon.CDK;

namespace EcsKinesisTaskRunner
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new EcsKinesisTaskRunnerStack(app, "EcsKinesisTaskRunner", new StackProps
            {
            });
            app.Synth();
        }
    }
}
