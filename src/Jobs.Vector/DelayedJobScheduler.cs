using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jobs.Vector;

/// <summary>
/// A structure representing a delayed job registration.
/// </summary>
public readonly record struct DelayedJob(JobItem Item, DateTimeOffset RunAt);

/// <summary>
/// A background hosted service that manages a time-sorted priority queue of delayed jobs,
/// promoting them to the active processing queue once their execution time is reached.
/// </summary>
public sealed class DelayedJobScheduler : BackgroundService
{
    private readonly ConcurrentQueue<DelayedJob> _incoming = new();
    private readonly PriorityQueue<JobItem, DateTimeOffset> _pq = new();
    private readonly IBackgroundJobQueue _activeQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DelayedJobScheduler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelayedJobScheduler"/> class.
    /// </summary>
    public DelayedJobScheduler(IBackgroundJobQueue activeQueue, ILogger<DelayedJobScheduler> logger, TimeProvider? timeProvider = null)
    {
        _activeQueue = activeQueue;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Schedules a job item to execute at a specific target time.
    /// </summary>
    /// <param name="item">The job item to execute.</param>
    /// <param name="runAt">The target execution time.</param>
    public void Schedule(JobItem item, DateTimeOffset runAt)
    {
        _incoming.Enqueue(new DelayedJob(item, runAt));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Drain incoming new delayed jobs into PriorityQueue
            while (_incoming.TryDequeue(out var delayedJob))
            {
                _pq.Enqueue(delayedJob.Item, delayedJob.RunAt);
            }

            var now = _timeProvider.GetUtcNow();

            // Enqueue all jobs that are due
            while (_pq.TryPeek(out var item, out var runAt) && now >= runAt)
            {
                _pq.Dequeue();
                await EnqueueActiveAsync(item, stoppingToken);
            }

            // Determine next delay
            var nextDelay = TimeSpan.FromMilliseconds(50);
            if (_pq.TryPeek(out _, out var nextRunAt))
            {
                var delay = nextRunAt - _timeProvider.GetUtcNow();
                if (delay > TimeSpan.Zero)
                {
                    nextDelay = delay < TimeSpan.FromMilliseconds(50) ? delay : TimeSpan.FromMilliseconds(50);
                }
                else
                {
                    nextDelay = TimeSpan.Zero;
                }
            }

            if (nextDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(nextDelay, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async ValueTask EnqueueActiveAsync(JobItem item, CancellationToken ct)
    {
        if (_activeQueue is BackgroundJobQueue queue)
        {
            await queue.EnqueueJobItemAsync(item, ct);
        }
        else
        {
            await _activeQueue.EnqueueAsync(item.Job, item.JobId, ct);
        }
    }
}
