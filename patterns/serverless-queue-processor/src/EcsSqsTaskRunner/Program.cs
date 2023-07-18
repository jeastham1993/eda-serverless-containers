using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EcsSqsTaskRunner
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new EcsSqsTaskRunnerStack(app, "EcsTaskRunner", new StackProps
            {
            });
            app.Synth();
        }
    }
}
