using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.ElastiCache;
using Amazon.CDK.AWS.ServiceDiscovery;
using Amazon.CDK.AWS.ApplicationAutoScaling;
using Constructs;
using System.Collections.Generic;
using System.Linq;

namespace Titan.Infra
{
    public class TitanStack : Stack
    {
        public TitanStack(Constructs.Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
        {
            // 1. VPC: MaxAzs = 2, Public and Private subnets
            var vpc = new Vpc(this, "TitanVpc", new VpcProps
            {
                MaxAzs = 2,
                NatGateways = 1 // Optimization for cost
            });

            // 2. Cluster
            var cluster = new Cluster(this, "TitanCluster", new ClusterProps
            {
                Vpc = vpc
            });

            // 3. Service Discovery (Cloud Map)
            var dnsNamespace = new PrivateDnsNamespace(this, "TitanNamespace", new PrivateDnsNamespaceProps
            {
                Name = "titan.local",
                Vpc = vpc
            });

            // 4. Redis (ElastiCache)
            var redisSg = new SecurityGroup(this, "RedisSg", new SecurityGroupProps
            {
                Vpc = vpc,
                Description = "Allow Redis port 6379 from VPC",
                AllowAllOutbound = true
            });
            redisSg.AddIngressRule(Peer.Ipv4(vpc.VpcCidrBlock), Port.Tcp(6379), "Allow Redis from VPC");

            var redisSubnetGroup = new CfnSubnetGroup(this, "RedisSubnetGroup", new CfnSubnetGroupProps
            {
                Description = "Subnet group for Redis",
                SubnetIds = vpc.PrivateSubnets.Select(s => s.SubnetId).ToArray()
            });

            var redis = new CfnCacheCluster(this, "TitanRedis", new CfnCacheClusterProps
            {
                CacheNodeType = "cache.t3.micro",
                Engine = "redis",
                NumCacheNodes = 1,
                VpcSecurityGroupIds = new[] { redisSg.SecurityGroupId },
                CacheSubnetGroupName = redisSubnetGroup.Ref
            });

            // 5. Master Service (Fargate)
            var masterService = new ApplicationLoadBalancedFargateService(this, "MasterService", new ApplicationLoadBalancedFargateServiceProps
            {
                Cluster = cluster,
                Cpu = 512,
                MemoryLimitMiB = 1024,
                DesiredCount = 1,
                PublicLoadBalancer = true,
                ListenerPort = 80,
                TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
                {
                    Image = ContainerImage.FromAsset("../Titan.Master"),
                    ContainerPort = 80,
                    Environment = new Dictionary<string, string>
                    {
                        { "ConnectionStrings__Redis", $"{redis.AttrRedisEndpointAddress}:{redis.AttrRedisEndpointPort}" }
                    },
                    LogDriver = LogDriver.AwsLogs(new AwsLogDriverProps { StreamPrefix = "TitanMaster" })
                },
                CloudMapOptions = new CloudMapOptions
                {
                    Name = "titan-master",
                    CloudMapNamespace = dnsNamespace
                }
            });

            // Expose gRPC port 5001
            masterService.TaskDefinition.DefaultContainer?.AddPortMappings(new PortMapping
            {
                ContainerPort = 5001,
                Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
            });

            // Health Check
            masterService.TargetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
            {
                Path = "/",
                Port = "80"
            });

            // 6. Worker Service (Fargate)
            var workerTaskDef = new FargateTaskDefinition(this, "WorkerTaskDef", new FargateTaskDefinitionProps
            {
                Cpu = 512,
                MemoryLimitMiB = 1024
            });

            var workerContainer = workerTaskDef.AddContainer("WorkerContainer", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromAsset("../Titan.Worker"),
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps { StreamPrefix = "TitanWorker" })
            });
            
            // Pass MasterUrl
            workerContainer.AddEnvironment("MasterUrl", $"http://titan-master.{dnsNamespace.NamespaceName}:5001");

            var workerService = new FargateService(this, "WorkerService", new FargateServiceProps
            {
                Cluster = cluster,
                TaskDefinition = workerTaskDef,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_NAT },
                DesiredCount = 1
            });

            // Scaling
            var scaling = workerService.AutoScaleTaskCount(new EnableScalingProps
            {
                MinCapacity = 1,
                MaxCapacity = 5
            });
            scaling.ScaleOnCpuUtilization("CpuScaling", new CpuUtilizationScalingProps
            {
                TargetUtilizationPercent = 75
            });

            // 7. Security Groups
            // Master SG allows Inbound 5001 from Worker SG
            var masterSg = masterService.Service.Connections.SecurityGroups[0];
            var workerSg = workerService.Connections.SecurityGroups[0];

            masterSg.AddIngressRule(workerSg, Port.Tcp(5001), "Allow gRPC from Workers");

            // Output
            new CfnOutput(this, "LoadBalancerDNS", new CfnOutputProps
            {
                Value = masterService.LoadBalancer.LoadBalancerDnsName
            });
        }
    }
}
