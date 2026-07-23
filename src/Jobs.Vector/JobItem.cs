using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jobs.Vector;

/// <summary>
/// Represents a job item held in the queue.
/// </summary>
public class JobItem
{
    /// <summary>
    /// Gets the unique identifier of the job.
    /// </summary>
    public string JobId { get; }

    /// <summary>
    /// Gets a delegate representing the execution logic of the job.
    /// </summary>
    public Func<CancellationToken, Task> Job => _delegateJob ?? ExecuteAsync;

    /// <summary>
    /// Gets or sets the maximum number of retries.
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the backoff retry delay.
    /// </summary>
    public TimeSpan RetryBackoff { get; set; }

    /// <summary>
    /// Gets or sets whether to use exponential backoff instead of linear.
    /// </summary>
    public bool RetryExponential { get; set; }

    /// <summary>
    /// Gets or sets the current attempt count.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobItem"/> class.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    public JobItem(string jobId)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobItem"/> class for a standard job delegate.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="job">The asynchronous delegate representing the job execution logic.</param>
    public JobItem(string jobId, Func<CancellationToken, Task> job) : this(jobId)
    {
        _delegateJob = job ?? throw new ArgumentNullException(nameof(job));
    }

    private readonly Func<CancellationToken, Task>? _delegateJob;

    /// <summary>
    /// Executes the underlying job delegate.
    /// </summary>
    /// <param name="ct">The cancellation token for the job execution.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous job execution.</returns>
    public virtual Task ExecuteAsync(CancellationToken ct)
    {
        if (_delegateJob != null)
        {
            return _delegateJob(ct);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents a state-passing job item stored in the queue without delegate wrapper allocations.
/// </summary>
/// <typeparam name="TState">The type of the state object passed to the delegate.</typeparam>
public sealed class StateJobItem<TState> : JobItem
{
    private readonly Func<TState, CancellationToken, Task> _job;
    private readonly TState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateJobItem{TState}"/> class.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="job">The state-passing asynchronous delegate.</param>
    /// <param name="state">The state object passed to the delegate.</param>
    public StateJobItem(string jobId, Func<TState, CancellationToken, Task> job, TState state) : base(jobId)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _state = state;
    }

    /// <inheritdoc />
    public override Task ExecuteAsync(CancellationToken ct)
    {
        return _job(_state, ct);
    }
}
