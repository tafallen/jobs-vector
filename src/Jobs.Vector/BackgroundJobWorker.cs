using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// A hosted service that executes background jobs using a configurable pool of concurrent worker loops.
/// Supports per-job cancellation, automatic retry with configurable backoff, OpenTelemetry
/// diagnostics via <see cref="ActivitySource"/>, and lifecycle event callbacks.
/// </summary>
public class BackgroundJobWorker : BackgroundService
{
    /// <summary>
    /// The <see cref="ActivitySource"/> used for OpenTelemetry tracing of job execution.
    /// Consumers can subscribe using an <see cref="System.Diagnostics.ActivityListener"/> configured
    /// for the <c>Jobs.Vector</c> source name.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Jobs.Vector", "1.0.0");

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
        // Build a linked CTS combining the shutdown token with any per-job cancellation token
        var jobCts = _queue is BackgroundJobQueue queue
            ? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, queue.GetJobCancellationToken(item.JobId))
            : CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        using (jobCts)
        {
            var jobToken = jobCts.Token;

            while (true)
            {
                using var activity = ActivitySource.StartActivity(
                    "job.execute",
                    ActivityKind.Internal,
                    parentContext: default,
                    tags: new[] { new KeyValuePair<string, object?>("job.id", item.JobId) });

                _logger.LogInformation("Background job {JobId} started processing (attempt {Attempt})", item.JobId, item.AttemptCount + 1);
                _statusStore.SetStatus(item.JobId, JobStatus.Processing);
                (_queue as BackgroundJobQueue)?.RaiseJobStarted(item.JobId);
                var timestamp = _timeProvider.GetTimestamp();

                try
                {
                    await item.ExecuteAsync(jobToken);

                    var duration = _timeProvider.GetElapsedTime(timestamp);
                    _statusStore.SetMetadata(item.JobId, "durationMs", duration.TotalMilliseconds);
                    _statusStore.SetStatus(item.JobId, JobStatus.Completed, 100);
                    _logger.LogInformation("Background job {JobId} completed successfully", item.JobId);

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    (_queue as BackgroundJobQueue)?.RaiseJobCompleted(item.JobId);
                    (_queue as BackgroundJobQueue)?.RemoveJobCancellationToken(item.JobId);
                    return true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutdown-triggered cancellation — takes priority, do not retry
                    var duration = _timeProvider.GetElapsedTime(timestamp);
                    _logger.LogWarning("Background job {JobId} cancelled by shutdown while processing", item.JobId);
                    _statusStore.SetMetadata(item.JobId, "durationMs", duration.TotalMilliseconds);
                    _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: "Cancelled by shutdown.");
                    activity?.SetStatus(ActivityStatusCode.Error, "Cancelled by shutdown.");
                    return false;
                }
                catch (OperationCanceledException) when (jobToken.IsCancellationRequested)
                {
                    // Per-job cancellation — treat as failed, do not retry
                    var duration = _timeProvider.GetElapsedTime(timestamp);
                    _logger.LogWarning("Background job {JobId} was cancelled by request", item.JobId);
                    _statusStore.SetMetadata(item.JobId, "durationMs", duration.TotalMilliseconds);
                    _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: "Cancelled by request.");
                    activity?.SetStatus(ActivityStatusCode.Error, "Cancelled by request.");
                    (_queue as BackgroundJobQueue)?.RemoveJobCancellationToken(item.JobId);
                    return true;
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddTag("exception.type", ex.GetType().FullName);
                    activity?.AddTag("exception.message", ex.Message);

                    item.AttemptCount++;

                    if (item.AttemptCount <= item.MaxRetries && !stoppingToken.IsCancellationRequested)
                    {
                        var backoff = CalculateBackoff(item);
                        _logger.LogWarning(ex, "Background job {JobId} failed on attempt {Attempt}. Retrying in {Backoff}ms",
                            item.JobId, item.AttemptCount, backoff.TotalMilliseconds);
                        _statusStore.SetStatus(item.JobId, JobStatus.Queued, error: $"Retry {item.AttemptCount}/{item.MaxRetries}: {ex.Message}");

                        if (backoff > TimeSpan.Zero)
                        {
                            // Delayed retry: hand off to the scheduler and yield this worker thread
                            (_queue as BackgroundJobQueue)?.ScheduleRetry(item, backoff);
                            return true;
                        }
                        // Zero-backoff: loop inline to avoid channel round-trip overhead
                        continue;
                    }

                    var finalDuration = _timeProvider.GetElapsedTime(timestamp);
                    _logger.LogError(ex, "Background job {JobId} failed after {Attempts} attempt(s)", item.JobId, item.AttemptCount);
                    _statusStore.SetMetadata(item.JobId, "durationMs", finalDuration.TotalMilliseconds);
                    _statusStore.SetStatus(item.JobId, JobStatus.Failed, error: ex.ToString());
                    (_queue as BackgroundJobQueue)?.RaiseJobFailed(item.JobId, ex);
                    (_queue as BackgroundJobQueue)?.RemoveJobCancellationToken(item.JobId);
                    return true;
                }
            }
        }
    }

    private static TimeSpan CalculateBackoff(JobItem item)
    {
        if (item.RetryBackoff == TimeSpan.Zero)
            return TimeSpan.Zero;

        if (item.RetryExponential)
        {
            // 2^(attempt-1) * backoff, capped at 30 seconds
            var multiplier = 1 << (item.AttemptCount - 1);
            var ms = item.RetryBackoff.TotalMilliseconds * multiplier;
            return TimeSpan.FromMilliseconds(Math.Min(ms, 30_000));
        }

        return item.RetryBackoff;
    }
}
