using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// A hosted service that runs background workers consuming jobs from <see cref="IBackgroundJobQueue"/>.
/// </summary>
public class BackgroundJobWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IJobStatusStore _statusStore;
    private readonly JobsOptions _options;
    private readonly ILogger<BackgroundJobWorker> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundJobWorker"/> class.
    /// </summary>
    /// <param name="queue">The queue containing enqueued background jobs.</param>
    /// <param name="statusStore">The store to record job status transitions.</param>
    /// <param name="options">The configuration options for background jobs.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    /// <param name="timeProvider">The provider used to query timestamps and elapsed time. Defaults to <see cref="TimeProvider.System"/>.</param>
    public BackgroundJobWorker(
        IBackgroundJobQueue queue,
        IJobStatusStore statusStore,
        IOptions<JobsOptions> options,
        ILogger<BackgroundJobWorker> logger,
        TimeProvider? timeProvider = null)
    {
        _queue = queue;
        _statusStore = statusStore;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
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

            _logger.LogInformation("Background job {JobId} started processing", item.JobId);
            _statusStore.SetStatus(item.JobId, JobStatus.Processing);
            var timestamp = _timeProvider.GetTimestamp();
            try
            {
                await item.Job(stoppingToken);
                var duration = _timeProvider.GetElapsedTime(timestamp);
                _statusStore.SetMetadata(item.JobId, new Dictionary<string, object>
                {
                    ["durationMs"] = duration.TotalMilliseconds
                });
                _statusStore.SetStatus(item.JobId, JobStatus.Completed, 100);
                _logger.LogInformation("Background job {JobId} completed successfully", item.JobId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                var duration = _timeProvider.GetElapsedTime(timestamp);
                _logger.LogWarning("Background job {JobId} cancelled by shutdown while processing", item.JobId);
                _statusStore.SetMetadata(item.JobId, new Dictionary<string, object>
                {
                    ["durationMs"] = duration.TotalMilliseconds
                });
                _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: "Cancelled by shutdown.");
                return;
            }
            catch (Exception ex)
            {
                var duration = _timeProvider.GetElapsedTime(timestamp);
                _logger.LogError(ex, "Background job {JobId} failed", item.JobId);
                _statusStore.SetMetadata(item.JobId, new Dictionary<string, object>
                {
                    ["durationMs"] = duration.TotalMilliseconds
                });
                _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: ex.ToString());
            }
        }
    }
}


