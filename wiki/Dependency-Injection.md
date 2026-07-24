# Dependency Injection

Jobs.Vector integrates cleanly with `Microsoft.Extensions.DependencyInjection` via a single extension method.

## Registration

```csharp
// The recommended way — binds from IConfiguration
builder.Services.AddBackgroundJobs(builder.Configuration);

// Alias (identical behaviour)
builder.Services.AddInMemoryJobQueue(builder.Configuration);
```

Both methods register the same set of services.

---

## What Gets Registered

| Service | Lifetime | Description |
|---|---|---|
| `IOptions<JobsOptions>` | Singleton | Configuration options with validation on startup. |
| `TimeProvider` | Singleton | `TimeProvider.System` (injectable for testing). |
| `IJobStatusStore` | Singleton | `InMemoryJobStatusStore` — thread-safe, TTL-evicted. |
| `BackgroundJobQueue` | Singleton | Concrete queue type (registered separately for DI wiring). |
| `IBackgroundJobQueue` | Singleton | Factory-created via `BackgroundJobQueue` + `SetScheduler`. |
| `DelayedJobScheduler` | Singleton + Hosted | Priority-queue backed delayed job scheduler. |
| `BackgroundJobWorker` | Hosted | One or more worker execution loops. |
| `JobStatusSweepWorker` | Hosted | Background TTL sweep/eviction service. |

---

## Injecting Services

### In a Controller

```csharp
[ApiController]
[Route("jobs")]
public class JobController(
    IBackgroundJobQueue jobQueue,
    IJobStatusStore statusStore) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Enqueue(CancellationToken ct)
    {
        var jobId = Guid.NewGuid();
        await jobQueue.EnqueueAsync(async jct => await DoWork(jct), jobId, ct);
        return Accepted(new { jobId });
    }

    [HttpGet("{jobId}")]
    public IActionResult Status(string jobId)
    {
        var snap = statusStore.GetStatus(jobId);
        return snap is null ? NotFound() : Ok(snap);
    }
}
```

### In a Minimal API

```csharp
app.MapPost("/jobs", async (IBackgroundJobQueue queue, CancellationToken ct) =>
{
    var jobId = Guid.NewGuid();
    await queue.EnqueueAsync(async jct => await DoWork(jct), jobId, ct);
    return Results.Accepted($"/jobs/{jobId}", new { jobId });
});

app.MapGet("/jobs/{jobId}", (string jobId, IJobStatusStore store) =>
    store.GetStatus(jobId) is { } snap ? Results.Ok(snap) : Results.NotFound());
```

### In a Background Service

```csharp
public class SchedulerService(IBackgroundJobQueue queue) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await queue.EnqueueAsync(
                async ct => await RunNightlySync(ct),
                jobId: $"nightly-{DateTime.UtcNow:yyyyMMdd}");

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

---

## Circular Dependency Resolution

`BackgroundJobQueue` and `DelayedJobScheduler` have a mutual dependency (the queue needs the scheduler to defer jobs; the scheduler needs the queue to re-enqueue them). This is resolved by DI using a **post-construction setter**:

```csharp
services.AddSingleton<BackgroundJobQueue>();
services.AddSingleton<IBackgroundJobQueue>(sp =>
{
    var queue = sp.GetRequiredService<BackgroundJobQueue>();
    var scheduler = sp.GetRequiredService<DelayedJobScheduler>();
    queue.SetScheduler(scheduler);   // Breaks the circular dependency
    return queue;
});
services.AddSingleton<DelayedJobScheduler>();
```

This is handled automatically by `AddBackgroundJobs()` — you don't need to configure it manually.

---

## Replacing the Status Store

To replace `InMemoryJobStatusStore` with a custom implementation (e.g., Redis, SQL):

```csharp
builder.Services.AddBackgroundJobs(builder.Configuration);

// Override the registration after AddBackgroundJobs:
builder.Services.AddSingleton<IJobStatusStore, RedisJobStatusStore>();
```

`AddBackgroundJobs` uses `AddSingleton` (not `TryAddSingleton`) for `IJobStatusStore`, so you must override after the call.

---

## Replacing the TimeProvider

For testability, you can replace `TimeProvider.System` with a `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`:

```csharp
// In test setup:
var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
services.AddSingleton<TimeProvider>(fakeTime);
services.AddBackgroundJobs(configuration);
```

`AddBackgroundJobs` uses `TryAddSingleton` for `TimeProvider`, so a prior registration wins.
