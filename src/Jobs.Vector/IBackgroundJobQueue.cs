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
    /// Dequeues a job item from the queue, blocking if necessary until one becomes available.
    /// </summary>
    /// <param name="ct">A cancellation token to cancel the dequeue operation.</param>
    /// <returns>A <see cref="ValueTask{JobItem}"/> containing the dequeued job item.</returns>
    ValueTask<JobItem> DequeueAsync(CancellationToken ct);
}

