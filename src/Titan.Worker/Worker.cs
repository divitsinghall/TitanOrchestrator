using Grpc.Core;
using Grpc.Net.Client;
using Polly;
using Polly.Retry;
using Titan.Shared.Protos;

namespace Titan.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly AsyncRetryPolicy _retryPolicy;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Resilience: Exponential backoff (2s, 4s, 8s)
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Connection failed. Retrying in {TimeSpan}s. Attempt {RetryCount}. Error: {Message}", 
                        timeSpan.TotalSeconds, retryCount, exception.Message);
                });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var masterUrl = _configuration["MasterUrl"] ?? "http://localhost:5000";
        var workerId = Guid.NewGuid().ToString();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogInformation("Connecting to Master at {MasterUrl}...", masterUrl);
                    using var channel = GrpcChannel.ForAddress(masterUrl);
                    var client = new JobService.JobServiceClient(channel);

                    using var stream = client.Connect();
                    
                    // Send ConnectRequest
                    await stream.RequestStream.WriteAsync(new WorkerMessage
                    {
                        Connect = new ConnectRequest
                        {
                            WorkerId = workerId,
                            CpuCores = Environment.ProcessorCount
                        }
                    });
                    
                    _logger.LogInformation("Connected to Master. Waiting for jobs...");

                    // Read loop
                    await foreach (var job in stream.ResponseStream.ReadAllAsync(stoppingToken))
                    {
                        _logger.LogInformation("Received Job {JobId}. Processing...", job.JobId);
                        
                        // Simulation: Random delay 1-5 seconds
                        var delay = Random.Shared.Next(1000, 5001);
                        await Task.Delay(delay, stoppingToken);
                        
                        _logger.LogInformation("Job {JobId} completed.", job.JobId);
                        
                        // Send Result
                        await stream.RequestStream.WriteAsync(new WorkerMessage
                        {
                            Result = new JobResult
                            {
                                JobId = job.JobId,
                                Success = true,
                                Message = "Completed successfully"
                            }
                        });
                    }
                });
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Fatal error in worker loop. Restarting connection loop...");
                await Task.Delay(5000, stoppingToken); // Wait before restarting the outer loop
            }
        }
    }
}
