using Amazon.CDK;

namespace Titan.Infra
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new TitanStack(app, "TitanStack", new StackProps
            {
                // For more information, see https://docs.aws.amazon.com/cdk/latest/guide/environments.html
                Env = new Amazon.CDK.Environment
                {
                    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
                }
            });
            app.Synth();
        }
    }
}
