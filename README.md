# Jobs.Vector

A lightweight, portable .NET 8 background job queue and worker hosting service built on standard .NET primitives (`System.Threading.Channels` and `BackgroundService`). 

It processes CPU-intensive and long-running operations asynchronously using bounded channels for backpressure, supports multi-threaded concurrent execution loops, and offers a thread-safe in-memory job status store with configurable time-to-live (TTL) eviction.

---

## Technical Architecture

### 1. Bounded Backpressure via `System.Threading.Channels`
`BackgroundJobQueue` utilizes C# Channels configured in `BoundedChannelFullMode.Wait` mode. If the queue fills up to its maximum capacity, the enqueuing thread blocks and waits rather than letting the memory heap grow out of control.

### 2. Spanning Multi-Thread Worker Loops
On application startup, `BackgroundJobWorker` reads the configured worker count and spawns that number of independent `Task` execution loops. The workers poll the channel concurrently:
*   A thread-safe dequeue removes the job item.
*   The worker updates the status store to `Processing`.
*   The delegate closure is executed inside a structured try/catch block.
*   Once finished, the state changes to `Completed` (or `Failed` with the exception message).

### 3. Thread-Safe Status Store with TTL Pruning
`InMemoryJobStatusStore` uses a `ConcurrentDictionary` to cache job outcomes. To prevent memory leaks, jobs have an associated TTL retention window:
*   **Lazy Eviction:** Calling `GetStatus(jobId)` checks the timestamp; if the job has expired, it is removed immediately.
*   **Active Cleanup:** `JobStatusSweepWorker` runs a background task that sweeps the dictionary and removes expired entries every minute.

---

## How to Use

### 1. Registering the Background Services
Bind configuration options and register workers:

```csharp
using Jobs.Vector;

// Registers IBackgroundJobQueue, IJobStatusStore, and starts the BackgroundJobWorker & JobStatusSweepWorker
builder.Services.AddBackgroundJobs(builder.Configuration);
```

Configure via `appsettings.json`:
```json
{
  "Jobs": {
    "Workers": 2,
    "QueueCapacity": 100,
    "StatusRetention": "00:30:00"
  }
}
```

### 2. Enqueuing a Job
Inject `IBackgroundJobQueue` and enqueue an asynchronous task closure:

```csharp
using Jobs.Vector;

public class IngestionController
{
    private readonly IBackgroundJobQueue _jobQueue;
    private readonly IJobStatusStore _statusStore;

    public IngestionController(IBackgroundJobQueue jobQueue, IJobStatusStore statusStore)
    {
        _jobQueue = jobQueue;
        _statusStore = statusStore;
    }

    public async Task StartIngestion(string fileId, CancellationToken ct)
    {
        string jobId = Guid.NewGuid().ToString();

        // Enqueue job delegate containing processing task context
        await _jobQueue.EnqueueAsync(async (jobCt) =>
        {
            // Simulated work logic
            await Task.Delay(5000, jobCt);
            _statusStore.SetImportResult(jobId, peopleImported: 42, eventsImported: 99);
        }, jobId, ct);
    }
}
```

### 3. Polling Job Status
Query the status store using the unique `JobId`:

```csharp
using Jobs.Vector;

public JobStatusSnapshot? GetJobStatus(string jobId)
{
    return _statusStore.GetStatus(jobId);
}
```

---

## License

Distributed under the PolyForm Noncommercial License 1.0.0. See [LICENSE](LICENSE) for details.
