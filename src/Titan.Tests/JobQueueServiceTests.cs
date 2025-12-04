using Moq;
using StackExchange.Redis;
using Titan.Master.Services;
using Titan.Shared.Protos;
using Xunit;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Collections.Concurrent;

namespace Titan.Tests;

public class JobQueueServiceTests
{
    [Fact]
    public async Task EnqueueJobAsync_ShouldAddJobToQueue()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<JobQueueService>>();
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDatabase = new Mock<IDatabase>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);

        var service = new JobQueueService(mockLogger.Object, mockRedis.Object);
        var job = new JobRequest { JobId = "job-1", Payload = "Data" };

        // Act
        await service.EnqueueJobAsync(job);

        // Assert
        // Use reflection to check private _jobQueue
        var fieldInfo = typeof(JobQueueService).GetField("_jobQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        var jobQueue = fieldInfo?.GetValue(service) as ConcurrentQueue<JobRequest>;

        Assert.NotNull(jobQueue);
        Assert.Single(jobQueue!);
        
        jobQueue!.TryPeek(out var queuedJob);
        Assert.NotNull(queuedJob);
        Assert.Equal("job-1", queuedJob!.JobId);
    }
}
