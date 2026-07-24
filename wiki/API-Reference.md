# API Reference

Complete reference for all public types in the `Jobs.Vector` namespace.

---

## `IBackgroundJobQueue`

The primary interface for enqueueing and managing jobs.

### Enqueue Methods

```csharp
// Standard (closure)
ValueTask EnqueueAsync(Func<CancellationToken, Task> job, string jobId, CancellationToken ct = default);
ValueTask EnqueueAsync(Func<CancellationToken, Task> job, Guid jobId, CancellationToken ct = default);
ValueTask EnqueueAsync(Func<CancellationToken, Task> job, long jobId, CancellationToken ct = default);

// State-passing (zero-closure)
ValueTask EnqueueAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, string jobId, CancellationToken ct = default);
ValueTask EnqueueAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, Guid jobId, CancellationToken ct = default);
ValueTask EnqueueAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, long jobId, CancellationToken ct = default);
```

### Delayed Enqueue Methods

```csharp
// Standard (closure)
ValueTask EnqueueDelayedAsync(Func<CancellationToken, Task> job, TimeSpan delay, string jobId, CancellationToken ct = default);
ValueTask EnqueueDelayedAsync(Func<CancellationToken, Task> job, TimeSpan delay, Guid jobId, CancellationToken ct = default);
ValueTask EnqueueDelayedAsync(Func<CancellationToken, Task> job, TimeSpan delay, long jobId, CancellationToken ct = default);

// State-passing (zero-closure)
ValueTask EnqueueDelayedAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, string jobId, CancellationToken ct = default);
ValueTask EnqueueDelayedAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, Guid jobId, CancellationToken ct = default);
ValueTask EnqueueDelayedAsync<TState>(Func<TState, CancellationToken, Task> job, TState state, TimeSpan delay, long jobId, CancellationToken ct = default);
```

### Cancellation

```csharp
bool CancelJob(string jobId);
bool CancelJob(Guid jobId);     // Extension method — normalizes to jobId.ToString("N")
bool CancelJob(long jobId);     // Extension method — normalizes to jobId.ToString()
```

Returns `true` if the CTS was found and cancelled; `false` if the job is unknown (completed or not found).

### Events

```csharp
event Action<string>? OnJobEnqueued;
event Action<string>? OnJobStarted;
event Action<string>? OnJobCompleted;
event Action<string, Exception>? OnJobFailed;
```

---

## `IJobStatusStore`

Thread-safe status and metadata store.

```csharp
void SetStatus(string jobId, JobStatus status, int progress = 0, string? error = null);
void SetMetadata(string jobId, IReadOnlyDictionary<string, object> metadata);
void SetMetadata(string jobId, string key, object value);
JobStatusSnapshot? GetStatus(string jobId);
void PruneExpired();
```

### Extension Methods (`JobIdExtensions`)

```csharp
// Guid overloads
void SetStatus(this IJobStatusStore store, Guid jobId, JobStatus status, int progress = 0, string? error = null);
JobStatusSnapshot? GetStatus(this IJobStatusStore store, Guid jobId);

// Long overloads
void SetStatus(this IJobStatusStore store, long jobId, JobStatus status, int progress = 0, string? error = null);
JobStatusSnapshot? GetStatus(this IJobStatusStore store, long jobId);
```

---

## `JobStatusSnapshot`

An immutable snapshot of job state. Returned by `IJobStatusStore.GetStatus`.

```csharp
public sealed class JobStatusSnapshot
{
    public JobStatus Status { get; }
    public int Progress { get; }               // 0–100
    public string? Error { get; }
    public IReadOnlyDictionary<string, object> Metadata { get; }
}
```

### Extension Methods

```csharp
// Typed metadata access:
T? GetValue<T>(this JobStatusSnapshot snapshot, string key);
```

Returns `default(T)` if the key is missing or the cast fails.

---

## `JobStatus` (enum)

```csharp
public enum JobStatus
{
    Queued,       // Accepted and waiting in the channel (or pending retry)
    Processing,   // Currently executing
    Completed,    // Finished successfully
    Failed        // Threw an exception or was cancelled
}
```

---

## `JobsOptions`

Configuration options bound from the `Jobs` configuration section.

```csharp
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    public int Workers { get; set; } = 1;
    public int QueueCapacity { get; set; } = 100;
    public TimeSpan StatusRetention { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
    public int DefaultMaxRetries { get; set; } = 0;
    public TimeSpan DefaultRetryBackoff { get; set; } = TimeSpan.Zero;
    public bool DefaultRetryExponential { get; set; } = false;
}
```

---

## `BackgroundJobQueue`

The concrete implementation of `IBackgroundJobQueue`. Prefer injecting `IBackgroundJobQueue` rather than `BackgroundJobQueue` directly.

### Notable Internal Methods (advanced use)

```csharp
// Wire the delayed scheduler post-construction (called automatically by DI)
void SetScheduler(DelayedJobScheduler scheduler);

// Cancellation token access (used by BackgroundJobWorker)
CancellationToken GetJobCancellationToken(string jobId);
void RemoveJobCancellationToken(string jobId);

// Internal re-enqueue for retries
ValueTask EnqueueJobItemAsync(JobItem item, CancellationToken ct = default);

// Schedule a retry via the delayed scheduler
void ScheduleRetry(JobItem item, TimeSpan backoff);

// Event raisers (called by BackgroundJobWorker)
void RaiseJobStarted(string jobId);
void RaiseJobCompleted(string jobId);
void RaiseJobFailed(string jobId, Exception ex);
```

---

## `InMemoryJobStatusStore`

```csharp
// Default constructor (for tests — uses TimeProvider.System and default options)
public InMemoryJobStatusStore();

// Full constructor
public InMemoryJobStatusStore(TimeProvider timeProvider, IOptions<JobsOptions> options);
```

---

## `DelayedJobScheduler`

A `BackgroundService` that manages deferred job scheduling.

```csharp
public class DelayedJobScheduler : BackgroundService
{
    public DelayedJobScheduler(BackgroundJobQueue queue, ILogger<DelayedJobScheduler> logger);
    
    // Schedule a job item to be promoted after the given backoff (used by BackgroundJobWorker)
    public void ScheduleJob(JobItem item, DateTimeOffset runAt);
    
    // Enqueue a new delayed job (called by IBackgroundJobQueue.EnqueueDelayedAsync)
    public void EnqueueDelayed(JobItem item, DateTimeOffset runAt);
}
```

---

## `JobsServiceCollectionExtensions`

```csharp
// Primary registration method
IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration);

// Alias (identical to AddBackgroundJobs)
IServiceCollection AddInMemoryJobQueue(this IServiceCollection services, IConfiguration configuration);
```

---

## `JobItem`

Represents a single unit of work flowing through the queue.

```csharp
public sealed class JobItem
{
    public string JobId { get; }
    public Func<CancellationToken, Task> Job { get; }
    
    // Retry configuration (set from JobsOptions at enqueue time)
    public int MaxRetries { get; init; }
    public TimeSpan RetryBackoff { get; init; }
    public bool RetryExponential { get; init; }
    
    // Mutable retry state
    public int AttemptCount { get; set; }
    
    // Execute the delegate
    public Task ExecuteAsync(CancellationToken ct);
}
```

> **Note:** `JobItem` is a public type but is not part of the primary API surface — you interact with it through `IBackgroundJobQueue`. It is documented here for completeness.
