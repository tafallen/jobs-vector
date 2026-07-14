namespace Jobs.Vector;

/// <summary>
/// Configuration options for background jobs.
/// </summary>
public class JobsOptions
{
    /// <summary>
    /// The configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Jobs";

    /// <summary>
    /// Gets or sets the number of concurrent worker execution loops to spawn.
    /// </summary>
    public int Workers { get; set; } = 1;

    /// <summary>
    /// Gets or sets how long a job's status remains queryable after its last update before it is
    /// evicted from the in-memory store. The AC requires at least one hour.
    /// </summary>
    public TimeSpan StatusRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of queued-but-not-yet-dequeued jobs. Once full, EnqueueAsync
    /// waits for a worker to dequeue an item before accepting a new one, providing
    /// backpressure so an upload burst can't grow memory without bound.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets how often JobStatusSweepWorker sweeps InMemoryJobStatusStore for entries past
    /// StatusRetention, independent of GetStatus lookup traffic.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
}

