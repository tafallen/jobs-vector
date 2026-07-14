namespace Jobs.Vector;

public class JobsOptions
{
    public const string SectionName = "Jobs";

    public int Workers { get; set; } = 1;

    /// <summary>
    /// How long a job's status remains queryable after its last update before it is
    /// evicted from the in-memory store. The AC requires at least one hour.
    /// </summary>
    public TimeSpan StatusRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of queued-but-not-yet-dequeued jobs. Once full, EnqueueAsync
    /// waits for a worker to dequeue an item before accepting a new one, providing
    /// backpressure so an upload burst can't grow memory without bound.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// How often JobStatusSweepWorker sweeps InMemoryJobStatusStore for entries past
    /// StatusRetention, independent of GetStatus lookup traffic.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
}
