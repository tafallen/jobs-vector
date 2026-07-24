# Job Status and Metadata

`IJobStatusStore` provides a thread-safe, TTL-evicted in-memory store for tracking job state, progress, and arbitrary metadata.

## Job Status Lifecycle

```
[Enqueued] → Queued → Processing → Completed
                ↑           ↓
              Queued ← (retry)
                            ↓
                          Failed
```

| Status | Meaning |
|---|---|
| `Queued` | Job accepted and waiting in the channel (or awaiting retry). |
| `Processing` | A worker has dequeued the job and is executing the delegate. |
| `Completed` | The delegate returned successfully. |
| `Failed` | The delegate threw an unhandled exception (or was cancelled). |

---

## Getting a Status Snapshot

```csharp
JobStatusSnapshot? snapshot = statusStore.GetStatus(jobId);

if (snapshot is null)
{
    // Job not found — either never created, or TTL expired and evicted.
    return NotFound();
}

Console.WriteLine($"Status:   {snapshot.Status}");
Console.WriteLine($"Progress: {snapshot.Progress}");
Console.WriteLine($"Error:    {snapshot.Error}");
```

`GetStatus` performs **lazy eviction**: if the TTL has expired, it removes the entry and returns `null`.

---

## Setting Status Within a Job

Your delegate can update progress by calling `SetStatus`:

```csharp
await jobQueue.EnqueueAsync(async ct =>
{
    statusStore.SetStatus(jobId, JobStatus.Processing, progress: 25);
    await DoPhase1Async(ct);

    statusStore.SetStatus(jobId, JobStatus.Processing, progress: 75);
    await DoPhase2Async(ct);
    
    // Worker sets to Completed after the delegate returns.
}, jobId);
```

---

## Metadata

Metadata is a key-value store (`IReadOnlyDictionary<string, object>`) attached to each job entry. Use it to store job-specific output, URLs, or counters.

### Setting Metadata

```csharp
// Single key/value — no dictionary allocation:
statusStore.SetMetadata(jobId, "reportUrl", "https://...");
statusStore.SetMetadata(jobId, "recordsProcessed", 15000);

// Batch update:
statusStore.SetMetadata(jobId, new Dictionary<string, object>
{
    ["reportUrl"] = "https://...",
    ["recordsProcessed"] = 15000
});
```

Metadata is **merged** — calling `SetMetadata` twice with different keys accumulates both entries.

### Reading Metadata

```csharp
var snapshot = statusStore.GetStatus(jobId)!;

// Weakly typed:
object? url = snapshot.Metadata["reportUrl"];

// Strongly typed (extension method):
string? url = snapshot.GetValue<string>("reportUrl");
int count = snapshot.GetValue<int>("recordsProcessed");
```

The `GetValue<T>` extension method returns `default(T)` if the key is missing or the type doesn't match.

---

## Convenience Extension Methods

`JobIdExtensions` provides strongly-typed overloads on `IJobStatusStore` for `Guid` and `long` IDs:

```csharp
// Guid overload
statusStore.SetStatus(guidJobId, JobStatus.Processing, progress: 50);
statusStore.SetStatus(guidJobId, JobStatus.Completed);

// Long overload
statusStore.SetStatus(longJobId, JobStatus.Failed, error: "Disk full");
```

---

## TTL Eviction

Job status entries are evicted after `StatusRetention` (default: 1 hour) using two mechanisms:

1. **Lazy eviction** — `GetStatus` checks the timestamp; if expired, removes and returns `null`.
2. **Active sweep** — `JobStatusSweepWorker` periodically calls `PruneExpired()` on the configured `SweepInterval`.

If a job's status is never polled, the sweep worker will clean it up automatically.

---

## Snapshot Caching

`JobStatusSnapshot` objects are cached inside `InMemoryJobStatusStore` entries and reused until the next `SetStatus` or `SetMetadata` call invalidates the cache. This makes repeated `GetStatus` calls allocation-free for non-updated jobs.

---

## Thread Safety

All `IJobStatusStore` methods are thread-safe:

- `SetStatus` and `SetMetadata` use per-entry locking (`lock (_lock)`) for mutation.
- `GetStatus` is lock-free for the snapshot cache read.
- Entries are stored in a `ConcurrentDictionary`.
