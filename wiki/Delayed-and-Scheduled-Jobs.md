# Delayed and Scheduled Jobs

Jobs.Vector supports deferring job execution by a specified `TimeSpan` using the built-in `DelayedJobScheduler`.

## How It Works

```
EnqueueDelayedAsync(job, delay: 10min, jobId)
    → Status set to Queued immediately
    → Job added to DelayedJobScheduler's PriorityQueue
    → At T+10min: scheduler promotes job to active channel
    → Worker picks it up and executes normally
```

The `DelayedJobScheduler` is a `BackgroundService` that runs a polling loop every **50 ms**. It maintains a time-sorted `PriorityQueue<JobItem, DateTimeOffset>` for efficient next-job lookup (O(log n) enqueue, O(log n) dequeue).

---

## Basic Usage

```csharp
// Fire-and-forget after a delay
await jobQueue.EnqueueDelayedAsync(async ct =>
{
    await SendFollowUpEmailAsync(userId, ct);
}, delay: TimeSpan.FromMinutes(30), jobId: Guid.NewGuid());
```

The job's status is set to `Queued` immediately. You can poll it the same way as regular jobs — it moves to `Processing` when the delay expires and a worker picks it up.

---

## All Overloads

### Standard (closure)

```csharp
// String ID
await jobQueue.EnqueueDelayedAsync(
    async ct => { ... },
    delay: TimeSpan.FromHours(1),
    jobId: "deferred-001");

// Guid ID
await jobQueue.EnqueueDelayedAsync(
    async ct => { ... },
    delay: TimeSpan.FromMinutes(5),
    jobId: Guid.NewGuid());

// Long ID
await jobQueue.EnqueueDelayedAsync(
    async ct => { ... },
    delay: TimeSpan.FromSeconds(30),
    jobId: 42L);
```

### State-passing (zero-closure allocation)

```csharp
await jobQueue.EnqueueDelayedAsync(
    static async (MyData data, CancellationToken ct) =>
    {
        await ProcessAsync(data, ct);
    },
    state: myData,
    delay: TimeSpan.FromMinutes(10),
    jobId: Guid.NewGuid());
```

---

## Use Cases

| Scenario | Delay |
|---|---|
| Send a welcome email 5 minutes after signup | `TimeSpan.FromMinutes(5)` |
| Expire a reservation after 15 minutes | `TimeSpan.FromMinutes(15)` |
| Run a report at end-of-business day | Calculated from now to 17:00 |
| Retry a failed webhook call in 1 hour | `TimeSpan.FromHours(1)` |

---

## Precision and Resolution

- The scheduler polls every **50 ms**, so the effective resolution is ~50 ms.
- Clock is provided by `TimeProvider` (injectable for testing with `FakeTimeProvider`).
- Delays of less than 50 ms will fire on the next scheduler tick.

---

## Checking Status Before Execution

Delayed jobs are assigned `Queued` status immediately on enqueue. Their `metadata` does not include a scheduled-run-at timestamp by default, but you can capture it in metadata within the delegate.

```csharp
var jobId = Guid.NewGuid();
var runAt = DateTime.UtcNow.AddMinutes(10);

await jobQueue.EnqueueDelayedAsync(async ct =>
{
    statusStore.SetMetadata(jobId, "actualStartedAt", DateTime.UtcNow.ToString("o"));
    await DoWorkAsync(ct);
}, delay: runAt - DateTime.UtcNow, jobId);

// Before the delay expires:
statusStore.GetStatus(jobId)?.Status  // → Queued

// After execution completes:
statusStore.GetStatus(jobId)?.Status  // → Completed
```

---

## Interaction with Retries

If a delayed job fails and `DefaultMaxRetries > 0` with a `DefaultRetryBackoff`, the retry is scheduled through the same `DelayedJobScheduler`. This means both initial delays and retry backoffs go through the same priority queue — ensuring consistent, precise scheduling across the board.

---

## Service Registration

`DelayedJobScheduler` is automatically registered when you call `AddBackgroundJobs()`. You do not need to register it separately.

```csharp
// This is all you need:
builder.Services.AddBackgroundJobs(builder.Configuration);
```

The DI wire-up uses a factory pattern to break the circular dependency between `BackgroundJobQueue` and `DelayedJobScheduler`, calling `queue.SetScheduler(scheduler)` after both are constructed.
