namespace Jobs.Vector;

public interface IJobStatusStore
{
    void SetStatus(string jobId, JobStatus status, int progress = 0, string? error = null);

    // Merges the given key/value pairs into the job's metadata dictionary without disturbing
    // its status/progress/error. Status transitions (including the terminal Completed/Failed)
    // remain the worker's job.
    void SetMetadata(string jobId, IReadOnlyDictionary<string, object> metadata);

    JobStatusSnapshot? GetStatus(string jobId);

    // Sweeps every entry past its retention window, independent of GetStatus lookup traffic --
    // a job whose status is set once and never polled again would otherwise never be evicted.
    // Called periodically by JobStatusSweepWorker.
    void PruneExpired();
}
