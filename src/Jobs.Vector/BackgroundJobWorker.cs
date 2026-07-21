using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// A hosted service that executes background jobs using a configurable pool of concurrent worker loops.
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
            JobItem? item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!await ProcessJobItemAsync(item, stoppingToken))
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested && _queue.TryDequeue(out item))
            {
                if (!await ProcessJobItemAsync(item, stoppingToken))
                {
                    return;
                }
            }
        }
    }

    private async ValueTask<bool> ProcessJobItemAsync(JobItem item, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background job {JobId} started processing", item.JobId);
        _statusStore.SetStatus(item.JobId, JobStatus.Processing);
        var timestamp = _timeProvider.GetTimestamp();
        try
        {
            await item.ExecuteAsync(stoppingToken);
            var duration = _timeProvider.GetElapsedTime(timestamp);
            _statusStore.SetMetadata(item.JobId, "durationMs", duration.TotalMilliseconds);
            _statusStore.SetStatus(item.JobId, JobStatus.Completed, 100);
            _logger.LogInformation("Background job {JobId} completed successfully", item.JobId);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            var duration = _timeProvider.GetElapsedTime(timestamp);
            _logger.LogWarning("Background job {JobId} cancelled by shutdown while processing", item.JobId);
            _statusStore.SetMetadata(item.JobId, "durationMs", duration.TotalMilliseconds);
            _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: "Cancelled by shutdown.");
            return false;
        }
        catch (Exception ex)
        {
            var duration = _timeProvider.GetElapsedTime(timestamp);
            _logger.LogError(ex, "Background job {JobId} failed", item.JobId);
            _statusStore.SetMetadata(item.JobId, "durationMs", duration.TotalMilliseconds);
            _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: ex.ToString());
            return true;
        }
    }
}
