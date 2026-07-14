using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

public class BackgroundJobWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IJobStatusStore _statusStore;
    private readonly JobsOptions _options;
    private readonly ILogger<BackgroundJobWorker> _logger;

    public BackgroundJobWorker(IBackgroundJobQueue queue, IJobStatusStore statusStore, IOptions<JobsOptions> options, ILogger<BackgroundJobWorker> logger)
    {
        _queue = queue;
        _statusStore = statusStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background job worker starting with {WorkerCount} worker(s)", _options.Workers);
        var workerLoops = Enumerable.Range(0, _options.Workers).Select(_ => RunWorkerLoopAsync(stoppingToken));
        return Task.WhenAll(workerLoops);
    }

    private async Task RunWorkerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            JobItem item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _statusStore.SetStatus(item.JobId, JobStatus.Processing);
            try
            {
                await item.Job(stoppingToken);
                _statusStore.SetStatus(item.JobId, JobStatus.Completed, 100);
                _logger.LogDebug("Background job {JobId} completed successfully", item.JobId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Background job {JobId} cancelled by shutdown while processing", item.JobId);
                _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: "Cancelled by shutdown.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background job {JobId} failed", item.JobId);
                _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: ex.Message);
            }
        }
    }
}
