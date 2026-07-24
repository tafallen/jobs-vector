# Without DI

You can use Jobs.Vector without any dependency injection framework by instantiating the components directly.

## Minimal Setup

```csharp
using Jobs.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var options = Options.Create(new JobsOptions
{
    Workers = 2,
    QueueCapacity = 100,
    StatusRetention = TimeSpan.FromMinutes(30),
    DefaultMaxRetries = 3
});

var statusStore = new InMemoryJobStatusStore(TimeProvider.System, options);
var queue = new BackgroundJobQueue(statusStore, options, NullLogger<BackgroundJobQueue>.Instance);

// For delayed jobs — wire the scheduler:
var schedulerLogger = NullLogger<DelayedJobScheduler>.Instance;
var scheduler = new DelayedJobScheduler(queue, schedulerLogger);
queue.SetScheduler(scheduler);

// Start the scheduler and worker manually:
using var cts = new CancellationTokenSource();

var schedulerTask = scheduler.StartAsync(cts.Token);
var workerLogger = NullLogger<BackgroundJobWorker>.Instance;
var worker = new BackgroundJobWorker(queue, statusStore, options, workerLogger);
var workerTask = worker.StartAsync(cts.Token);

// Enqueue work
await queue.EnqueueAsync(async ct =>
{
    await Task.Delay(500, ct);
    statusStore.SetStatus("job-1", JobStatus.Completed, 100);
}, "job-1");

// Wait for it...
await Task.Delay(1000);

var snapshot = statusStore.GetStatus("job-1");
Console.WriteLine($"Status: {snapshot?.Status}"); // Completed

// Shut down
await cts.CancelAsync();
await worker.StopAsync(CancellationToken.None);
await scheduler.StopAsync(CancellationToken.None);
```

---

## Console Application

```csharp
using Jobs.Vector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var options = Options.Create(new JobsOptions { Workers = 1, QueueCapacity = 50 });
var statusStore = new InMemoryJobStatusStore(TimeProvider.System, options);
var queue = new BackgroundJobQueue(
    statusStore,
    options,
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<BackgroundJobQueue>());

var done = new TaskCompletionSource();

await queue.EnqueueAsync(async ct =>
{
    Console.WriteLine("Job started");
    await Task.Delay(2000, ct);
    Console.WriteLine("Job done");
    done.SetResult();
}, "console-job-1");

var workerLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<BackgroundJobWorker>();
var worker = new BackgroundJobWorker(queue, statusStore, options, workerLogger);

using var cts = new CancellationTokenSource();
await worker.StartAsync(cts.Token);

await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
await worker.StopAsync(CancellationToken.None);
```

---

## Unit Test Setup

```csharp
using Jobs.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Fast, lightweight test setup — no hosted service lifetime needed.
var options = Options.Create(new JobsOptions { Workers = 1, QueueCapacity = 10 });
var statusStore = new InMemoryJobStatusStore();  // No-arg ctor uses defaults
var queue = new BackgroundJobQueue(statusStore, options, NullLogger<BackgroundJobQueue>.Instance);
var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

await queue.EnqueueAsync(ct => Task.CompletedTask, "test-job");
await worker.StartAsync(CancellationToken.None);

// Wait for completion...
var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
while (statusStore.GetStatus("test-job")?.Status != JobStatus.Completed
    && DateTime.UtcNow < deadline)
    await Task.Delay(10);

await worker.StopAsync(CancellationToken.None);
Assert.Equal(JobStatus.Completed, statusStore.GetStatus("test-job")!.Status);
```

---

## Component Overview (Without DI)

```
BackgroundJobQueue         — channel, CTS registry, scheduler wiring
    ↓ SetScheduler(...)
DelayedJobScheduler        — PriorityQueue for deferred jobs
    ↓ StartAsync(ct)

BackgroundJobWorker        — spawns N worker loops
    ↓ StartAsync(ct)

InMemoryJobStatusStore     — ConcurrentDictionary<string, Entry>

JobStatusSweepWorker       — optional, call PruneExpired() manually if not hosted
```

### Manual Pruning

If you don't run `JobStatusSweepWorker`, call `statusStore.PruneExpired()` periodically yourself:

```csharp
// Every 15 minutes
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(15));
        statusStore.PruneExpired();
    }
});
```
