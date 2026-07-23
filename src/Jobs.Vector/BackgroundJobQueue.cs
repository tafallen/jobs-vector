using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// A channel-backed implementation of <see cref="IBackgroundJobQueue"/> providing bounded backpressure,
/// per-job cancellation, lifecycle event callbacks, and delayed job scheduling.
/// </summary>
public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<JobItem> _channel;
    private readonly IJobStatusStore _statusStore;
    private readonly ILogger<BackgroundJobQueue> _logger;
    private readonly JobsOptions _options;
    private readonly int _queueCapacity;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeJobs = new(StringComparer.Ordinal);

    // Lazy reference — set by DI via SetScheduler when running in the full DI stack.
    // Tests that don't need delayed jobs leave this null; attempting EnqueueDelayedAsync without
    // a scheduler will throw InvalidOperationException.
    private DelayedJobScheduler? _scheduler;

    /// <summary>
    /// Sets the <see cref="DelayedJobScheduler"/> to be used by this queue.
    /// Called by the DI container to break the circular dependency between
    /// <see cref="BackgroundJobQueue"/> and <see cref="DelayedJobScheduler"/>.
    /// </summary>
    public void SetScheduler(DelayedJobScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public event Action<string>? OnJobEnqueued;

    /// <inheritdoc />
    public event Action<string>? OnJobStarted;

    /// <inheritdoc />
    public event Action<string>? OnJobCompleted;

    /// <inheritdoc />
    public event Action<string, Exception>? OnJobFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundJobQueue"/> class.
    /// </summary>
    public BackgroundJobQueue(
        IJobStatusStore statusStore,
        IOptions<JobsOptions> options,
        ILogger<BackgroundJobQueue> logger)
    {
        _statusStore = statusStore;
        _logger = logger;
        _options = options.Value;
        _queueCapacity = _options.QueueCapacity;
        _channel = Channel.CreateBounded<JobItem>(new BoundedChannelOptions(_queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = _options.Workers == 1,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// Raises the <see cref="OnJobStarted"/> event for the specified job.
    /// </summary>
    public void RaiseJobStarted(string jobId) => OnJobStarted?.Invoke(jobId);

    /// <summary>
    /// Raises the <see cref="OnJobCompleted"/> event for the specified job.
    /// </summary>
    public void RaiseJobCompleted(string jobId) => OnJobCompleted?.Invoke(jobId);

    /// <summary>
    /// Raises the <see cref="OnJobFailed"/> event for the specified job.
    /// </summary>
    public void RaiseJobFailed(string jobId, Exception ex) => OnJobFailed?.Invoke(jobId, ex);

    /// <summary>
    /// Returns the per-job <see cref="CancellationToken"/> registered for a running job.
    /// </summary>
    public CancellationToken GetJobCancellationToken(string jobId)
    {
        return _activeJobs.TryGetValue(jobId, out var cts) ? cts.Token : CancellationToken.None;
    }

    /// <summary>
    /// Removes and disposes the per-job <see cref="CancellationTokenSource"/> once a job finishes.
    /// </summary>
    public void RemoveJobCancellationToken(string jobId)
    {
        if (_activeJobs.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Schedules a job item for retry after the specified backoff via the delayed scheduler.
    /// </summary>
    public void ScheduleRetry(JobItem item, TimeSpan backoff)
    {
        GetScheduler().Schedule(item, DateTimeOffset.UtcNow.Add(backoff));
    }

    /// <summary>
    /// Enqueues an existing job item directly into the active processing channel.
    /// Used internally by the delayed scheduler and retry path.
    /// </summary>
    public ValueTask EnqueueJobItemAsync(JobItem item, CancellationToken ct = default)
    {
        return EnqueueJobItemInternalAsync(item, ct);
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(Func<CancellationToken, Task> job, string jobId, CancellationToken ct = default)
    {
        _statusStore.SetStatus(jobId, JobStatus.Queued);
        _activeJobs[jobId] = new CancellationTokenSource();

        var item = new JobItem(jobId, job)
        {
            MaxRetries = _options.DefaultMaxRetries,
            RetryBackoff = _options.DefaultRetryBackoff,
            RetryExponential = _options.DefaultRetryExponential
        };

        return EnqueueJobItemInternalAsync(item, ct);
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, string jobId, CancellationToken ct = default)
    {
        _statusStore.SetStatus(jobId, JobStatus.Queued);
        _activeJobs[jobId] = new CancellationTokenSource();

        var item = new StateJobItem<TState>(jobId, job, state)
        {
            MaxRetries = _options.DefaultMaxRetries,
            RetryBackoff = _options.DefaultRetryBackoff,
            RetryExponential = _options.DefaultRetryExponential
        };

        return EnqueueJobItemInternalAsync(item, ct);
    }

    /// <inheritdoc />
    public ValueTask EnqueueDelayedAsync(Func<CancellationToken, Task> job, TimeSpan delay, string jobId, CancellationToken ct = default)
    {
        _statusStore.SetStatus(jobId, JobStatus.Queued);
        _activeJobs[jobId] = new CancellationTokenSource();

        var item = new JobItem(jobId, job)
        {
            MaxRetries = _options.DefaultMaxRetries,
            RetryBackoff = _options.DefaultRetryBackoff,
            RetryExponential = _options.DefaultRetryExponential
        };

        GetScheduler().Schedule(item, DateTimeOffset.UtcNow.Add(delay));
        OnJobEnqueued?.Invoke(jobId);
        return default;
    }

    /// <inheritdoc />
    public ValueTask EnqueueDelayedAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, string jobId, CancellationToken ct = default)
    {
        _statusStore.SetStatus(jobId, JobStatus.Queued);
        _activeJobs[jobId] = new CancellationTokenSource();

        var item = new StateJobItem<TState>(jobId, job, state)
        {
            MaxRetries = _options.DefaultMaxRetries,
            RetryBackoff = _options.DefaultRetryBackoff,
            RetryExponential = _options.DefaultRetryExponential
        };

        GetScheduler().Schedule(item, DateTimeOffset.UtcNow.Add(delay));
        OnJobEnqueued?.Invoke(jobId);
        return default;
    }

    private ValueTask EnqueueJobItemInternalAsync(JobItem item, CancellationToken ct)
    {
        if (_channel.Writer.TryWrite(item))
        {
            _logger.LogInformation("Background job {JobId} successfully enqueued", item.JobId);
            OnJobEnqueued?.Invoke(item.JobId);
            return default;
        }

        return EnqueueAsyncSlowPath(item, item.JobId, ct);
    }

    private async ValueTask EnqueueAsyncSlowPath(JobItem item, string jobId, CancellationToken ct)
    {
        _logger.LogWarning("Job queue is at capacity ({QueueCapacity}). Applying backpressure to enqueuing thread for job {JobId}", _queueCapacity, jobId);
        await _channel.Writer.WriteAsync(item, ct);
        _logger.LogInformation("Background job {JobId} successfully enqueued", jobId);
        OnJobEnqueued?.Invoke(jobId);
    }

    /// <inheritdoc />
    public bool CancelJob(string jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            _logger.LogWarning("Job cancellation requested for JobId {JobId}", jobId);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public ValueTask<JobItem> DequeueAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAsync(ct);
    }

    /// <inheritdoc />
    public bool TryDequeue([NotNullWhen(true)] out JobItem? item)
    {
        return _channel.Reader.TryRead(out item);
    }

    private DelayedJobScheduler GetScheduler()
    {
        if (_scheduler is null)
            throw new InvalidOperationException("A DelayedJobScheduler has not been configured. Ensure AddBackgroundJobs() is used and the service is registered.");
        return _scheduler;
    }
}
