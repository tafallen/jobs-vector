using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Jobs.Vector;

/// <summary>
/// Defines a queue for managing and scheduling background jobs.
/// </summary>
public interface IBackgroundJobQueue
{
    /// <summary>
    /// Event triggered when a job is enqueued.
    /// </summary>
    event Action<string>? OnJobEnqueued;

    /// <summary>
    /// Event triggered when a job starts processing.
    /// </summary>
    event Action<string>? OnJobStarted;

    /// <summary>
    /// Event triggered when a job completes successfully.
    /// </summary>
    event Action<string>? OnJobCompleted;

    /// <summary>
    /// Event triggered when a job fails.
    /// </summary>
    event Action<string, Exception>? OnJobFailed;

    /// <summary>
    /// Enqueues a job delegate to be processed asynchronously.
    /// </summary>
    /// <param name="job">The asynchronous delegate representing the work to be performed.</param>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="ct">A cancellation token to cancel the enqueue operation itself.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask EnqueueAsync(Func<CancellationToken, Task> job, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a job delegate with state to be processed asynchronously without capture allocations.
    /// </summary>
    /// <typeparam name="TState">The type of the state object passed to the delegate.</typeparam>
    /// <param name="job">The asynchronous delegate representing the work to be performed.</param>
    /// <param name="state">The state object passed to the delegate.</param>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="ct">A cancellation token to cancel the enqueue operation itself.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask EnqueueAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a delayed job delegate to be executed after the specified delay.
    /// </summary>
    /// <param name="job">The asynchronous delegate representing the work to be performed.</param>
    /// <param name="delay">The delay duration before the job is executed.</param>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="ct">A cancellation token to cancel the enqueue operation itself.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask EnqueueDelayedAsync(Func<CancellationToken, Task> job, TimeSpan delay, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a delayed job delegate with state to be executed after the specified delay.
    /// </summary>
    /// <typeparam name="TState">The type of the state object passed to the delegate.</typeparam>
    /// <param name="job">The asynchronous delegate representing the work to be performed.</param>
    /// <param name="state">The state object passed to the delegate.</param>
    /// <param name="delay">The delay duration before the job is executed.</param>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="ct">A cancellation token to cancel the enqueue operation itself.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask EnqueueDelayedAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a running or queued job by its identifier.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job to cancel.</param>
    /// <returns><see langword="true"/> if the cancellation signal was successfully registered or sent; otherwise, <see langword="false"/>.</returns>
    bool CancelJob(string jobId);

    /// <summary>
    /// Dequeues a job item from the queue, blocking if necessary until one becomes available.
    /// </summary>
    /// <param name="ct">A cancellation token to cancel the dequeue operation.</param>
    /// <returns>A <see cref="ValueTask{JobItem}"/> containing the dequeued job item.</returns>
    ValueTask<JobItem> DequeueAsync(CancellationToken ct);

    /// <summary>
    /// Attempts to dequeue a job item from the queue immediately without blocking.
    /// </summary>
    /// <param name="item">When this method returns, contains the dequeued job item if available; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a job item was successfully dequeued; otherwise, <see langword="false"/>.</returns>
    bool TryDequeue([NotNullWhen(true)] out JobItem? item);
}
