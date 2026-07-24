# Lifecycle Events

`IBackgroundJobQueue` exposes four events you can subscribe to for monitoring, alerting, metrics collection, and integration with other systems.

## Event Summary

| Event | Signature | When |
|---|---|---|
| `OnJobEnqueued` | `Action<string>` | Job accepted into the queue. |
| `OnJobStarted` | `Action<string>` | Worker begins executing the job delegate. |
| `OnJobCompleted` | `Action<string>` | Job delegate returned without throwing. |
| `OnJobFailed` | `Action<string, Exception>` | Job delegate threw an unhandled exception (after all retries exhausted). |

All events pass the `jobId` (`string`) as the first argument. `OnJobFailed` additionally passes the final `Exception`.

---

## Subscribing to Events

```csharp
// Resolve the queue from DI
var queue = app.Services.GetRequiredService<IBackgroundJobQueue>();

// Subscribe with lambdas
queue.OnJobEnqueued  += jobId => logger.LogDebug("Enqueued: {JobId}", jobId);
queue.OnJobStarted   += jobId => logger.LogDebug("Started:  {JobId}", jobId);
queue.OnJobCompleted += jobId => logger.LogInformation("Done:  {JobId}", jobId);
queue.OnJobFailed    += (jobId, ex) =>
{
    logger.LogError(ex, "Failed: {JobId}", jobId);
    alertService.Raise($"Job {jobId} failed: {ex.Message}");
};
```

---

## Metrics Integration

Wire events to your metrics infrastructure during startup:

```csharp
var queue = app.Services.GetRequiredService<IBackgroundJobQueue>();
var meter = app.Services.GetRequiredService<IMeter>(); // or use static Meter

var jobsEnqueued   = meter.CreateCounter<long>("jobs_enqueued_total");
var jobsStarted    = meter.CreateCounter<long>("jobs_started_total");
var jobsCompleted  = meter.CreateCounter<long>("jobs_completed_total");
var jobsFailed     = meter.CreateCounter<long>("jobs_failed_total");

queue.OnJobEnqueued  += _ => jobsEnqueued.Add(1);
queue.OnJobStarted   += _ => jobsStarted.Add(1);
queue.OnJobCompleted += _ => jobsCompleted.Add(1);
queue.OnJobFailed    += (_, _) => jobsFailed.Add(1);
```

---

## SignalR / WebSocket Integration

Push real-time job updates to connected clients:

```csharp
var queue = app.Services.GetRequiredService<IBackgroundJobQueue>();
var hubContext = app.Services.GetRequiredService<IHubContext<JobStatusHub>>();

queue.OnJobCompleted += async jobId =>
{
    await hubContext.Clients.All.SendAsync("JobCompleted", jobId);
};

queue.OnJobFailed += async (jobId, ex) =>
{
    await hubContext.Clients.All.SendAsync("JobFailed", jobId, ex.Message);
};
```

---

## Event Firing Guarantees

| Event | Fired from | Thread-safe? |
|---|---|---|
| `OnJobEnqueued` | Enqueuing thread (caller of `EnqueueAsync`) | ✅ Yes |
| `OnJobStarted` | Worker thread | ✅ Yes |
| `OnJobCompleted` | Worker thread | ✅ Yes |
| `OnJobFailed` | Worker thread | ✅ Yes |

> **Note:** Event handlers should not block or throw. Handler exceptions are not caught by the library — they will propagate to the worker loop and cause unexpected behaviour. Use fire-and-forget patterns for async handlers.

---

## Retry Behaviour and Events

During retries:

- `OnJobFailed` is **only raised after all retries are exhausted** (final failure).
- `OnJobStarted` is raised on every attempt (including retries).
- `OnJobCompleted` is raised when a retry attempt succeeds.

---

## Unsubscribing

Events follow standard C# event semantics. Unsubscribe using `-=`:

```csharp
Action<string> handler = jobId => Console.WriteLine(jobId);
queue.OnJobEnqueued += handler;

// Later:
queue.OnJobEnqueued -= handler;
```

---

## Subscribing via a Service

For cleaner architecture, implement a hosted service that subscribes on startup:

```csharp
public class JobEventLogger(
    IBackgroundJobQueue queue,
    ILogger<JobEventLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        queue.OnJobCompleted += id => logger.LogInformation("Completed: {JobId}", id);
        queue.OnJobFailed    += (id, ex) => logger.LogError(ex, "Failed: {JobId}", id);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddHostedService<JobEventLogger>();
```
