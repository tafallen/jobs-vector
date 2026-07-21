using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// A channel-backed implementation of <see cref="IBackgroundJobQueue"/> providing bounded backpressure.
/// </summary>
public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<JobItem> _channel;
    private readonly IJobStatusStore _statusStore;
    private readonly ILogger<BackgroundJobQueue> _logger;
    private readonly int _queueCapacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundJobQueue"/> class.
    /// </summary>
    /// <param name="statusStore">The store used to persist job status updates.</param>
    /// <param name="options">The configuration options for background jobs.</param>
    /// <param name="logger">The logger for queue diagnostics.</param>
    public BackgroundJobQueue(IJobStatusStore statusStore, IOptions<JobsOptions> options, ILogger<BackgroundJobQueue> logger)
    {
        _statusStore = statusStore;
        _logger = logger;
        _queueCapacity = options.Value.QueueCapacity;
        _channel = Channel.CreateBounded<JobItem>(new BoundedChannelOptions(_queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = options.Value.Workers == 1,
            AllowSynchronousContinuations = false
        });
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(Func<CancellationToken, Task> job, string jobId, CancellationToken ct = default)
    {
        _statusStore.SetStatus(jobId, JobStatus.Queued);

        var item = new JobItem(jobId, job);
        if (_channel.Writer.TryWrite(item))
        {
            _logger.LogInformation("Background job {JobId} successfully enqueued", jobId);
            return default;
        }

        return EnqueueAsyncSlowPath(item, jobId, ct);
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, string jobId, CancellationToken ct = default)
    {
        _statusStore.SetStatus(jobId, JobStatus.Queued);

        var item = new StateJobItem<TState>(jobId, job, state);
        if (_channel.Writer.TryWrite(item))
        {
            _logger.LogInformation("Background job {JobId} successfully enqueued", jobId);
            return default;
        }

        return EnqueueAsyncSlowPath(item, jobId, ct);
    }

    private async ValueTask EnqueueAsyncSlowPath(JobItem item, string jobId, CancellationToken ct)
    {
        _logger.LogWarning("Job queue is at capacity ({QueueCapacity}). Applying backpressure to enqueuing thread for job {JobId}", _queueCapacity, jobId);
        await _channel.Writer.WriteAsync(item, ct);
        _logger.LogInformation("Background job {JobId} successfully enqueued", jobId);
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
}
