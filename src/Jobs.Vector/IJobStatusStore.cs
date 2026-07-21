namespace Jobs.Vector;

/// <summary>
/// Defines a store for managing and retrieving background job statuses and metadata.
/// </summary>
public interface IJobStatusStore
{
    /// <summary>
    /// Sets the status, progress, and error details of a job.
    /// </summary>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="status">The current status of the job.</param>
    /// <param name="progress">The execution progress of the job, typically from 0 to 100.</param>
    /// <param name="error">Optional error message if the job failed.</param>
    void SetStatus(string jobId, JobStatus status, int progress = 0, string? error = null);

    /// <summary>
    /// Merges the given key/value pairs into the job's metadata dictionary without disturbing its status, progress, or error.
    /// Status transitions remain the worker's responsibility.
    /// </summary>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="metadata">A dictionary of metadata to merge.</param>
    void SetMetadata(string jobId, IReadOnlyDictionary<string, object> metadata);

    /// <summary>
    /// Merges a single key/value pair into the job's metadata dictionary without disturbing its status, progress, or error.
    /// Bypasses dictionary instantiation for single-item metadata updates.
    /// </summary>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <param name="key">The metadata key to set.</param>
    /// <param name="value">The metadata value to set.</param>
    void SetMetadata(string jobId, string key, object value);

    /// <summary>
    /// Retrieves a snapshot of the job's status and metadata, performing lazy eviction if the job has expired.
    /// </summary>
    /// <param name="jobId">A unique identifier for the job.</param>
    /// <returns>A <see cref="JobStatusSnapshot"/> if the job exists and has not expired; otherwise, <see langword="null"/>.</returns>
    JobStatusSnapshot? GetStatus(string jobId);

    /// <summary>
    /// Sweeps all job entries and evicts any that have exceeded their configured retention duration.
    /// </summary>
    void PruneExpired();
}

