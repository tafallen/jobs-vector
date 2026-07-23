using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jobs.Vector;

/// <summary>
/// Extension methods providing convenient <see cref="long"/> and <see cref="Guid"/> overloads for job queuing and status tracking.
/// </summary>
public static class JobIdExtensions
{
    /// <summary>
    /// Enqueues a job delegate using a numeric job ID.
    /// </summary>
    public static ValueTask EnqueueAsync(this IBackgroundJobQueue queue, Func<CancellationToken, Task> job, long jobId, CancellationToken ct = default)
    {
        return queue.EnqueueAsync(job, jobId.ToString(), ct);
    }

    /// <summary>
    /// Enqueues a job delegate using a Guid job ID.
    /// </summary>
    public static ValueTask EnqueueAsync(this IBackgroundJobQueue queue, Func<CancellationToken, Task> job, Guid jobId, CancellationToken ct = default)
    {
        return queue.EnqueueAsync(job, jobId.ToString("N"), ct);
    }

    /// <summary>
    /// Enqueues a job delegate with state using a numeric job ID.
    /// </summary>
    public static ValueTask EnqueueAsync<TState>(this IBackgroundJobQueue queue, Func<TState, CancellationToken, Task> job, TState state, long jobId, CancellationToken ct = default)
    {
        return queue.EnqueueAsync(job, state, jobId.ToString(), ct);
    }

    /// <summary>
    /// Enqueues a job delegate with state using a Guid job ID.
    /// </summary>
    public static ValueTask EnqueueAsync<TState>(this IBackgroundJobQueue queue, Func<TState, CancellationToken, Task> job, TState state, Guid jobId, CancellationToken ct = default)
    {
        return queue.EnqueueAsync(job, state, jobId.ToString("N"), ct);
    }

    /// <summary>
    /// Sets the status, progress, and error details of a job using a numeric job ID.
    /// </summary>
    public static void SetStatus(this IJobStatusStore store, long jobId, JobStatus status, int progress = 0, string? error = null)
    {
        store.SetStatus(jobId.ToString(), status, progress, error);
    }

    /// <summary>
    /// Sets the status, progress, and error details of a job using a Guid job ID.
    /// </summary>
    public static void SetStatus(this IJobStatusStore store, Guid jobId, JobStatus status, int progress = 0, string? error = null)
    {
        store.SetStatus(jobId.ToString("N"), status, progress, error);
    }

    /// <summary>
    /// Merges the given key/value pairs into the job's metadata dictionary using a numeric job ID.
    /// </summary>
    public static void SetMetadata(this IJobStatusStore store, long jobId, IReadOnlyDictionary<string, object> metadata)
    {
        store.SetMetadata(jobId.ToString(), metadata);
    }

    /// <summary>
    /// Merges the given key/value pairs into the job's metadata dictionary using a Guid job ID.
    /// </summary>
    public static void SetMetadata(this IJobStatusStore store, Guid jobId, IReadOnlyDictionary<string, object> metadata)
    {
        store.SetMetadata(jobId.ToString("N"), metadata);
    }

    /// <summary>
    /// Merges a single key/value pair into the job's metadata dictionary using a numeric job ID.
    /// </summary>
    public static void SetMetadata(this IJobStatusStore store, long jobId, string key, object value)
    {
        store.SetMetadata(jobId.ToString(), key, value);
    }

    /// <summary>
    /// Merges a single key/value pair into the job's metadata dictionary using a Guid job ID.
    /// </summary>
    public static void SetMetadata(this IJobStatusStore store, Guid jobId, string key, object value)
    {
        store.SetMetadata(jobId.ToString("N"), key, value);
    }

    /// <summary>
    /// Retrieves a snapshot of the job's status and metadata using a numeric job ID.
    /// </summary>
    public static JobStatusSnapshot? GetStatus(this IJobStatusStore store, long jobId)
    {
        return store.GetStatus(jobId.ToString());
    }

    /// <summary>
    /// Retrieves a snapshot of the job's status and metadata using a Guid job ID.
    /// </summary>
    public static JobStatusSnapshot? GetStatus(this IJobStatusStore store, Guid jobId)
    {
        return store.GetStatus(jobId.ToString("N"));
    }

    /// <summary>
    /// Enqueues a delayed job delegate using a numeric job ID.
    /// </summary>
    public static ValueTask EnqueueDelayedAsync(this IBackgroundJobQueue queue, Func<CancellationToken, Task> job, TimeSpan delay, long jobId, CancellationToken ct = default)
    {
        return queue.EnqueueDelayedAsync(job, delay, jobId.ToString(), ct);
    }

    /// <summary>
    /// Enqueues a delayed job delegate using a Guid job ID.
    /// </summary>
    public static ValueTask EnqueueDelayedAsync(this IBackgroundJobQueue queue, Func<CancellationToken, Task> job, TimeSpan delay, Guid jobId, CancellationToken ct = default)
    {
        return queue.EnqueueDelayedAsync(job, delay, jobId.ToString("N"), ct);
    }

    /// <summary>
    /// Enqueues a delayed state-passing job delegate using a numeric job ID.
    /// </summary>
    public static ValueTask EnqueueDelayedAsync<TState>(this IBackgroundJobQueue queue, Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, long jobId, CancellationToken ct = default)
    {
        return queue.EnqueueDelayedAsync(job, state, delay, jobId.ToString(), ct);
    }

    /// <summary>
    /// Enqueues a delayed state-passing job delegate using a Guid job ID.
    /// </summary>
    public static ValueTask EnqueueDelayedAsync<TState>(this IBackgroundJobQueue queue, Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, Guid jobId, CancellationToken ct = default)
    {
        return queue.EnqueueDelayedAsync(job, state, delay, jobId.ToString("N"), ct);
    }

    /// <summary>
    /// Cancels a job by its numeric job ID.
    /// </summary>
    public static bool CancelJob(this IBackgroundJobQueue queue, long jobId)
    {
        return queue.CancelJob(jobId.ToString());
    }

    /// <summary>
    /// Cancels a job by its Guid job ID.
    /// </summary>
    public static bool CancelJob(this IBackgroundJobQueue queue, Guid jobId)
    {
        return queue.CancelJob(jobId.ToString("N"));
    }
}
