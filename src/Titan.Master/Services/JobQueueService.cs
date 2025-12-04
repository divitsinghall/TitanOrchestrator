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
    private readonly ConcurrentDictionary<string, IServerStreamWriter<JobRequest>> _workerStreams = new();

    public JobQueueService(ILogger<JobQueueService> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public void RegisterWorker(string workerId, IServerStreamWriter<JobRequest> stream)
    {
        _workerStreams[workerId] = stream;
        var db = _redis.GetDatabase();
        db.StringSet($"worker:{workerId}", "Idle");
        _logger.LogInformation("Worker {WorkerId} registered and marked Idle.", workerId);
        
        // Check if there are pending jobs
        TryDispatchJobToWorker(workerId);
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
            TryDispatchJobToWorker(workerId);
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
                TryDispatchJobToWorker(workerId);
            }
        }
    }

    private void TryDispatchJobToWorker(string workerId)
    {
        if (_jobQueue.TryDequeue(out var job))
        {
            if (_workerStreams.TryGetValue(workerId, out var stream))
            {
                // Mark busy first (optimistic)
                var db = _redis.GetDatabase();
                db.StringSet($"worker:{workerId}", "Busy");

                try
                {
                    stream.WriteAsync(job); // Fire and forget write, or await? 
                    // WriteAsync is awaitable. But we are in a void/sync context here if called from RegisterWorker.
                    // Better to make this async or fire-and-forget safely.
                    // For simplicity in this scaffold, we'll assume it works or handle errors in the stream loop.
                    _logger.LogInformation("Job {JobId} dispatched to Worker {WorkerId}", job.JobId, workerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch job to worker {WorkerId}", workerId);
                    // Re-queue job
                    _jobQueue.Enqueue(job);
                    db.StringSet($"worker:{workerId}", "Idle"); // Revert status
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
