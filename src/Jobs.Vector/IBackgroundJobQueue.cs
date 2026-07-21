using System.Diagnostics.CodeAnalysis;

namespace Jobs.Vector;

/// <summary>
/// Defines a queue for managing and scheduling background jobs.
/// </summary>
public interface IBackgroundJobQueue
{
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


