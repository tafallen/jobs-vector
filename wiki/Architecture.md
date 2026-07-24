# Architecture

This page describes the internal design and data flow of Jobs.Vector.

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│  Application                                                     │
│                                                                  │
│  IBackgroundJobQueue.EnqueueAsync(job, jobId)                   │
│  IBackgroundJobQueue.EnqueueDelayedAsync(job, delay, jobId)     │
│  IBackgroundJobQueue.CancelJob(jobId)                           │
└────────────────────────────┬────────────────────────────────────┘
                             │
               ┌─────────────▼──────────────┐
               │      BackgroundJobQueue     │
               │   ─────────────────────    │
               │  BoundedChannel<JobItem>   │
               │  ConcurrentDict<id, CTS>   │
               │  SetScheduler(scheduler)   │
               └──────┬─────────────┬───────┘
                      │             │ delayed
              ┌───────▼──────┐ ┌───▼────────────────────┐
              │  Active       │ │   DelayedJobScheduler   │
              │  Channel      │ │  ─────────────────────  │
              │  (immediate   │ │  PriorityQueue<Item,    │
              │   dispatch)   │ │  DateTimeOffset>        │
              └───────┬───────┘ │  polls every 50ms       │
                      │         └────────────┬────────────┘
                      │                      │ (on expiry)
                      └──────────┬───────────┘
                                 │
               ┌─────────────────▼──────────────────┐
               │         BackgroundJobWorker          │
               │   ────────────────────────────────  │
               │  N concurrent loops (configurable)  │
               │  Linked CTS (shutdown + per-job)    │
               │  ActivitySource("Jobs.Vector")      │
               │  Inline retry loop (zero-backoff)   │
               └─────────────────┬──────────────────┘
                                 │
               ┌─────────────────▼──────────────────┐
               │       InMemoryJobStatusStore        │
               │   ─────────────────────────────── │
               │  ConcurrentDict<string, Entry>     │
               │  Per-entry lock + snapshot cache   │
               │  Lazy + active TTL eviction        │
               └────────────────────────────────────┘
```

---

## 1. Channel and Backpressure (`BackgroundJobQueue`)

`BackgroundJobQueue` owns a `BoundedChannel<JobItem>` configured with:

- **Capacity**: `JobsOptions.QueueCapacity`
- **Full mode**: `BoundedChannelFullMode.Wait`
- **Single reader**: `false` if `Workers > 1`, `true` if `Workers == 1` (enables single-reader optimization)

When the channel is full, `WriteAsync` (called by `EnqueueAsync`) blocks the caller's `await` until a slot opens. This is the **backpressure mechanism** — it prevents runaway memory growth.

### JobItem Structure

```csharp
public sealed class JobItem
{
    public string JobId { get; }
    public Func<CancellationToken, Task> Job { get; }
    public int MaxRetries { get; init; }
    public TimeSpan RetryBackoff { get; init; }
    public bool RetryExponential { get; init; }
    public int AttemptCount { get; set; }        // Mutable — incremented on each retry
}
```

The same `JobItem` object reference is used across all retry attempts. `AttemptCount` is mutated in-place.

---

## 2. Cancellation Token Registry

`BackgroundJobQueue` maintains:

```csharp
private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeJobs;
```

- A new `CancellationTokenSource` is created for each job at `EnqueueAsync`.
- The worker creates a **linked** CTS: `CreateLinkedTokenSource(stoppingToken, jobCts.Token)`.
- `CancelJob(jobId)` calls `_activeJobs[jobId].Cancel()`.
- The CTS is disposed and removed on job completion, failure, or cancellation.

---

## 3. Delayed Job Scheduler (`DelayedJobScheduler`)

```csharp
// Simplified internal representation
private readonly PriorityQueue<JobItem, DateTimeOffset> _queue = new();
private readonly ConcurrentQueue<(JobItem, DateTimeOffset)> _staging = new();
```

**Polling loop** (every 50 ms):

1. Drain `_staging` → move items into `_queue` (thread-safe staging avoids lock contention).
2. Peek at `_queue.TryPeek` → if top item's priority ≤ `now`, dequeue and `EnqueueJobItemAsync`.
3. Repeat until no more items are due.

**Circular dependency resolution** — `BackgroundJobQueue` needs `DelayedJobScheduler` to schedule retries, and `DelayedJobScheduler` needs `BackgroundJobQueue` to promote jobs. This is resolved in DI via `SetScheduler(scheduler)` called post-construction:

```csharp
services.AddSingleton<IBackgroundJobQueue>(sp =>
{
    var queue = sp.GetRequiredService<BackgroundJobQueue>();
    var sched  = sp.GetRequiredService<DelayedJobScheduler>();
    queue.SetScheduler(sched);    // Breaks the cycle
    return queue;
});
```

---

## 4. Worker Execution Loop (`BackgroundJobWorker`)

Each worker task runs this loop:

```csharp
while (!stopping)
{
    await DequeueAsync(stopping);     // Blocks until item available
    
    while (TryDequeue(out var item))  // Drain synchronously while items available
    {
        await ProcessJobItemAsync(item, stopping);
    }
}
```

`ProcessJobItemAsync` uses an **inline `while(true)` retry loop** for zero-backoff retries:

```
while (true)
{
    try { await job(token); return true; }
    catch when (shutdown) { return false; }
    catch when (cancel) { return true; }
    catch (Exception ex)
    {
        if (AttemptCount <= MaxRetries && backoff == 0) continue;   // Inline retry
        if (AttemptCount <= MaxRetries && backoff >  0) ScheduleRetry(); return true;
        SetFailed(); return true;
    }
}
```

**OpenTelemetry**: Each iteration starts a new `Activity` (one per attempt). Activities are disposed at the end of each attempt, giving precise per-attempt timing.

---

## 5. Status Store (`InMemoryJobStatusStore`)

```
ConcurrentDictionary<string, Entry>
    Entry:
        - JobStatus Status
        - int Progress
        - string? Error
        - IReadOnlyDictionary<string, object> Metadata
        - DateTimeOffset UpdatedAt
        - object _lock
        - JobStatusSnapshot? _cachedSnapshot   ← nulled on each mutation
```

- **Reads**: `GetStatus` → returns cached `_cachedSnapshot` (no allocation if unchanged).
- **Writes**: `UpdateStatus` / `UpdateMetadata` → lock, mutate, null cache.
- **Eviction**: lazy on `GetStatus` + active via `PruneExpired` / `JobStatusSweepWorker`.

---

## Performance Characteristics

| Operation | Complexity | Notes |
|---|---|---|
| `EnqueueAsync` | O(1) amortized | Channel write + Dict insert |
| `DequeueAsync` | O(1) | Channel read |
| `CancelJob` | O(1) | Dict lookup + CTS.Cancel |
| `EnqueueDelayedAsync` | O(log n) | PriorityQueue enqueue |
| Scheduler tick | O(k log n) | k = jobs due this tick |
| `GetStatus` | O(1) | Dict + cached snapshot |
| `SetStatus` | O(1) | Lock + null cache |
| `PruneExpired` | O(n) | Full scan |
