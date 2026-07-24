# Enqueueing Jobs

`IBackgroundJobQueue` provides several overloads for enqueueing work, covering different ID types and allocation strategies.

## Standard Enqueue

The simplest overload: pass a `Func<CancellationToken, Task>` and a string job ID.

```csharp
await jobQueue.EnqueueAsync(async ct =>
{
    await DoSomeWorkAsync(ct);
}, jobId: "my-job-001");
```

- The cancellation token passed to your delegate is a **linked token** combining:
  - The application shutdown token (`IHostApplicationLifetime.ApplicationStopping`)
  - The per-job cancellation token (used by `CancelJob`)
- Status is set to `Queued` immediately on return, before the job starts.

---

## Guid and Long Job IDs

For convenience, overloads accept `Guid` and `long` job IDs. They are normalized to strings internally (`guid.ToString("N")` and `id.ToString()`):

```csharp
// Guid ID
var jobId = Guid.NewGuid();
await jobQueue.EnqueueAsync(async ct =>
{
    await ProcessAsync(ct);
}, jobId);

// long ID
long jobId = 42L;
await jobQueue.EnqueueAsync(async ct =>
{
    await ProcessAsync(ct);
}, jobId);
```

---

## State-Passing (Zero-Closure) Overloads

Closures in C# capture the enclosing scope by reference and allocate a heap object on each call. For high-throughput scenarios, use the **state-passing overloads** to avoid closure heap allocations:

```csharp
// Instead of capturing 'request' in a closure:
await jobQueue.EnqueueAsync(
    static async (ReportRequest req, CancellationToken ct) =>
    {
        await GenerateReportAsync(req, ct);
    },
    state: request,
    jobId: Guid.NewGuid());
```

The `static` keyword on the lambda enforces at compile time that no captures occur.

### State Overloads Available

| Method | Description |
|---|---|
| `EnqueueAsync<TState>(Func<TState, CancellationToken, Task>, TState, string, CancellationToken)` | State + string ID |
| `EnqueueAsync<TState>(Func<TState, CancellationToken, Task>, TState, Guid, CancellationToken)` | State + Guid ID |
| `EnqueueAsync<TState>(Func<TState, CancellationToken, Task>, TState, long, CancellationToken)` | State + long ID |

---

## Backpressure Behaviour

`EnqueueAsync` uses a bounded `System.Threading.Channels` channel configured with `BoundedChannelFullMode.Wait`.

When the channel is **full** (all `QueueCapacity` slots are occupied):

1. The calling thread **blocks asynchronously** (`await`) until a slot opens.
2. A warning is logged: `"Applying backpressure…"`.
3. The job is enqueued as soon as space is available.

This prevents uncontrolled memory growth under load. Configure `QueueCapacity` appropriately for your workload.

---

## Checking Status After Enqueue

The job ID is the key for `IJobStatusStore`:

```csharp
var jobId = Guid.NewGuid();
await jobQueue.EnqueueAsync(async ct => { ... }, jobId);

// Later...
var snapshot = statusStore.GetStatus(jobId.ToString("N"));

if (snapshot is null) return NotFound();

return Ok(new
{
    snapshot.Status,      // Queued | Processing | Completed | Failed
    snapshot.Progress,    // 0–100
    snapshot.Error,       // null or error message
    snapshot.Metadata     // IReadOnlyDictionary<string, object>
});
```

> **Note:** When using `Guid` IDs, the key in the status store is `guid.ToString("N")` (32 hex characters, no hyphens).

---

## Reporting Progress

Your job delegate can update progress via the `IJobStatusStore` captured in the closure:

```csharp
await jobQueue.EnqueueAsync(async ct =>
{
    for (int i = 0; i < 10; i++)
    {
        await ProcessBatchAsync(i, ct);
        statusStore.SetStatus(jobId, JobStatus.Processing, progress: (i + 1) * 10);
    }
}, jobId);
```

> **Important:** The worker itself sets status to `Processing` at the start and `Completed`/`Failed` at the end. Setting progress within your delegate is additive and will not be overwritten until job completion.

---

## Thread Safety

- All `EnqueueAsync` overloads are fully thread-safe.
- Multiple concurrent callers can enqueue jobs simultaneously.
- The channel serializes writes internally.
