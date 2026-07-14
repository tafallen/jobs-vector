namespace Jobs.Vector;

/// <summary>
/// Represents an enqueued background job.
/// </summary>
/// <param name="JobId">The unique identifier of the job.</param>
/// <param name="Job">The delegate containing the asynchronous work to run.</param>
public record JobItem(string JobId, Func<CancellationToken, Task> Job);

