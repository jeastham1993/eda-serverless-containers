using Amazon.CDK;

namespace EcsKinesisTaskRunner
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            var publisherStack = new PublisherStack(
                app,
                "PublisherStack");
            
            new ProcessorStack(app, "EcsKinesisTaskRunner", new ProcessorStackProps(publisherStack.DataStream));
            app.Synth();
        }
    }
}