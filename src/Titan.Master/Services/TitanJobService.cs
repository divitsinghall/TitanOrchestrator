using Grpc.Core;
using Titan.Shared.Protos;

namespace Titan.Master.Services;

public class TitanJobService : JobService.JobServiceBase
{
    private readonly ILogger<TitanJobService> _logger;
    private readonly JobQueueService _jobQueue;

    public TitanJobService(ILogger<TitanJobService> logger, JobQueueService jobQueue)
    {
        _logger = logger;
        _jobQueue = jobQueue;
    }

    public override async Task Connect(IAsyncStreamReader<WorkerMessage> requestStream, IServerStreamWriter<JobRequest> responseStream, ServerCallContext context)
    {
        string workerId = "Unknown";
        
        try
        {
            // Wait for the first message which should be ConnectRequest
            if (await requestStream.MoveNext())
            {
                var firstMessage = requestStream.Current;
                if (firstMessage.MessageCase == WorkerMessage.MessageOneofCase.Connect)
                {
                    workerId = firstMessage.Connect.WorkerId;
                    int cores = firstMessage.Connect.CpuCores;
                    _logger.LogInformation("Worker {WorkerId} connected with {Cores} cores.", workerId, cores);
                    
                    _jobQueue.RegisterWorker(workerId, responseStream);
                }
                else
                {
                    _logger.LogWarning("First message was not ConnectRequest. Aborting connection.");
                    return;
                }
            }

            // Loop to handle subsequent messages (JobResults)
            while (await requestStream.MoveNext())
            {
                var msg = requestStream.Current;
                if (msg.MessageCase == WorkerMessage.MessageOneofCase.Result)
                {
                    var result = msg.Result;
                    _logger.LogInformation("Received result for Job {JobId} from Worker {WorkerId}: Success={Success}, Msg={Msg}", 
                        result.JobId, workerId, result.Success, result.Message);
                    
                    // Mark worker as Idle so it can take more jobs
                    await _jobQueue.UpdateWorkerStatusAsync(workerId, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker {WorkerId} connection cancelled.", workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in worker connection {WorkerId}", workerId);
        }
        finally
        {
            _jobQueue.UnregisterWorker(workerId);
        }
    }
}
