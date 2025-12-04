using System.Collections.Concurrent;
using Grpc.Core;
using StackExchange.Redis;
using Titan.Shared.Protos;

namespace Titan.Master.Services;

public class JobQueueService
{
    private readonly ILogger<JobQueueService> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly ConcurrentQueue<JobRequest> _jobQueue = new();
    
    // Wrapper class to hold the stream and a semaphore for thread safety
    private class WorkerConnection
    {
        public IServerStreamWriter<JobRequest> Stream { get; }
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public WorkerConnection(IServerStreamWriter<JobRequest> stream)
        {
            Stream = stream;
        }
    }

    private readonly ConcurrentDictionary<string, WorkerConnection> _workerStreams = new();

    public JobQueueService(ILogger<JobQueueService> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task RegisterWorker(string workerId, IServerStreamWriter<JobRequest> stream)
    {
        _workerStreams[workerId] = new WorkerConnection(stream);
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"worker:{workerId}", "Idle");
        _logger.LogInformation("Worker {WorkerId} registered and marked Idle.", workerId);
        
        // Check if there are pending jobs
        await TryDispatchJobToWorkerAsync(workerId);
    }

    public void UnregisterWorker(string workerId)
    {
        _workerStreams.TryRemove(workerId, out _);
        var db = _redis.GetDatabase();
        db.KeyDelete($"worker:{workerId}");
        _logger.LogInformation("Worker {WorkerId} unregistered.", workerId);
    }

    public async Task EnqueueJobAsync(JobRequest job)
    {
        _jobQueue.Enqueue(job);
        _logger.LogInformation("Job {JobId} enqueued.", job.JobId);
        await DispatchJobsAsync();
    }

    public async Task UpdateWorkerStatusAsync(string workerId, bool isBusy)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"worker:{workerId}", isBusy ? "Busy" : "Idle");
        
        if (!isBusy)
        {
            // If worker became idle, try to give it a job
            await TryDispatchJobToWorkerAsync(workerId);
        }
    }

    private async Task DispatchJobsAsync()
    {
        // Simple dispatch logic: find idle workers and assign jobs
        var db = _redis.GetDatabase();
        foreach (var workerId in _workerStreams.Keys)
        {
            var status = await db.StringGetAsync($"worker:{workerId}");
            if (status == "Idle")
            {
                await TryDispatchJobToWorkerAsync(workerId);
            }
        }
    }

    private async Task TryDispatchJobToWorkerAsync(string workerId)
    {
        if (_jobQueue.TryDequeue(out var job))
        {
            if (_workerStreams.TryGetValue(workerId, out var connection))
            {
                // Mark busy first (optimistic)
                var db = _redis.GetDatabase();
                await db.StringSetAsync($"worker:{workerId}", "Busy");

                try
                {
                    // Thread-safe write
                    await connection.Lock.WaitAsync();
                    try
                    {
                        await connection.Stream.WriteAsync(job);
                    }
                    finally
                    {
                        connection.Lock.Release();
                    }
                    
                    _logger.LogInformation("Job {JobId} dispatched to Worker {WorkerId}", job.JobId, workerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch job to worker {WorkerId}", workerId);
                    // Re-queue job
                    _jobQueue.Enqueue(job);
                    await db.StringSetAsync($"worker:{workerId}", "Idle"); // Revert status
                }
            }
            else
            {
                // Worker disconnected? Re-queue
                _jobQueue.Enqueue(job);
            }
        }
    }
}
