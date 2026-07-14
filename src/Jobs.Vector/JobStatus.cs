namespace Jobs.Vector;

/// <summary>
/// Specifies the execution status of a background job.
/// </summary>
public enum JobStatus
{
    /// <summary>
    /// The job has been enqueued and is waiting for an available worker.
    /// </summary>
    Queued,

    /// <summary>
    /// The job has been dequeued and is currently executing.
    /// </summary>
    Processing,

    /// <summary>
    /// The job has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job failed due to an exception or cancellation.
    /// </summary>
    Failed,
}

