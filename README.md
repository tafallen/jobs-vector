# Jobs.Vector

A portable **.NET 8** background job queue and worker hosting service built on standard .NET primitives (`System.Threading.Channels` and `BackgroundService`).

It processes CPU-intensive and long-running operations asynchronously using bounded channels for backpressure, supports multi-threaded concurrent execution loops, per-job cancellation, delayed scheduling, automatic retry with exponential backoff, lifecycle event hooks, and native OpenTelemetry diagnostics.

[![CI](https://github.com/tafallen/jobs-vector/actions/workflows/ci.yml/badge.svg)](https://github.com/tafallen/jobs-vector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Jobs.Vector)](https://www.nuget.org/packages/Jobs.Vector)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)

---

## Features

- ⚡ **Bounded backpressure** — utilizes `System.Threading.Channels` with bounded capacity to block the enqueuing thread and prevent uncontrolled heap growth
- 👯 **Multi-threaded execution loops** — spawns configurable multiple concurrent background Task execution loops polling the channel
- ❌ **Per-job cancellation** — cancel any running or queued job on-demand by its `string`, `Guid`, or `long` job ID
- ⏰ **Delayed / scheduled jobs** — enqueue jobs to run after a specified delay via a `PriorityQueue`-backed background scheduler
- 🔄 **Automatic retries** — configurable max retries with linear or exponential backoff, resolved inline with zero channel round-trips
- 🪝 **Lifecycle event hooks** — subscribe to `OnJobEnqueued`, `OnJobStarted`, `OnJobCompleted`, and `OnJobFailed` events
- 📡 **OpenTelemetry diagnostics** — native `ActivitySource("Jobs.Vector")` tracing with `job.id`, exception type, and status tags
- 🔒 **Thread-safe job status cache** — in-memory `ConcurrentDictionary` store tracking job outcomes and progress safely
- 🧹 **Automatic TTL cleanup** — active background pruning and lazy-eviction to purge expired job status entries
- 📦 **NuGet-ready** — structured for `dotnet pack` with symbols (`.snupkg`)
- 💉 **DI-friendly** — integrates with `Microsoft.Extensions.DependencyInjection` via `AddBackgroundJobs()`
- 🛡️ **No app-specific dependencies** — relies only on standard .NET primitives; no databases or ORMs required

---

## Quick Start

### Install

```bash
dotnet add package Jobs.Vector
```

### Register with Dependency Injection

To register the background job services:
```csharp
// Program.cs / Startup.cs
builder.Services.AddBackgroundJobs(builder.Configuration);
```

Configure via `appsettings.json`:
```json
{
  "Jobs": {
    "Workers": 2,
    "QueueCapacity": 100,
    "StatusRetention": "00:30:00",
    "SweepInterval": "00:01:00",
    "DefaultMaxRetries": 3,
    "DefaultRetryBackoff": "00:00:05",
    "DefaultRetryExponential": true
  }
}
```

### Enqueuing a Job

Inject `IBackgroundJobQueue` and enqueue an asynchronous task. You can use standard delegates, `Guid`/`long` job IDs, or state-passing overloads to eliminate heap allocations:

```csharp
public class IngestionController(IBackgroundJobQueue jobQueue, IJobStatusStore statusStore)
{
    public async Task StartIngestionAsync(string fileId, CancellationToken ct)
    {
        // 1. Native Guid or long job IDs are supported directly
        Guid jobId = Guid.NewGuid();

        // 2. High-performance state-passing overload (0 closure class heap allocations)
        await jobQueue.EnqueueAsync(
            static async (fileId, jobCt) =>
            {
                // Simulated work logic
                await Task.Delay(5000, jobCt);
            },
            fileId,
            jobId,
            ct);

        // 3. Set single metadata entry (0 Dictionary heap allocations)
        statusStore.SetMetadata(jobId, "fileId", fileId);
    }
}
```

### Polling Job Status

Query the status store using `string`, `Guid`, or `long` job IDs, and retrieve strongly-typed metadata:

```csharp
public JobStatusSnapshot? GetJobStatus(Guid jobId)
{
    var snapshot = statusStore.GetStatus(jobId);
    if (snapshot != null)
    {
        // Retrieve strongly typed metadata values
        int peopleImported = snapshot.GetValue<int>("peopleImported");
        double durationMs = snapshot.GetValue<double>("durationMs");
    }
    return snapshot;
}
```

### Without DI (direct use)

```csharp
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Jobs.Vector;

var options = Options.Create(new JobsOptions
{
    Workers = 1,
    QueueCapacity = 10,
    StatusRetention = TimeSpan.FromMinutes(5)
});

var timeProvider = TimeProvider.System;
var statusStore = new InMemoryJobStatusStore(timeProvider, options);
var jobQueue = new BackgroundJobQueue(statusStore, options, NullLogger<BackgroundJobQueue>.Instance);

// Enqueue a job directly using long or Guid IDs
long jobId = 1001;
await jobQueue.EnqueueAsync(async (ct) =>
{
    await Task.Delay(1000, ct);
}, jobId);
```

---

## Advanced Features

### Per-Job Cancellation

Cancel a specific running or queued job on-demand using its `string`, `Guid`, or `long` job ID:

```csharp
// Enqueue a long-running job
Guid jobId = Guid.NewGuid();
await jobQueue.EnqueueAsync(async ct =>
{
    await Task.Delay(Timeout.Infinite, ct); // waits until cancelled
}, jobId);

// Cancel it by ID — the job's CancellationToken is triggered
bool wasCancelled = jobQueue.CancelJob(jobId);
```

Cancelled jobs have their status set to `Failed` with error `"Cancelled by request."`. Shutdown-triggered cancellation is handled separately and marked `"Cancelled by shutdown."`.

---

### Delayed / Scheduled Jobs

Enqueue a job to execute after a specified delay. The `DelayedJobScheduler` manages a time-sorted `PriorityQueue` and promotes jobs to the active processing queue when their delay expires:

```csharp
// Execute after 10 minutes
await jobQueue.EnqueueDelayedAsync(async ct =>
{
    await SendFollowUpEmailAsync(ct);
}, delay: TimeSpan.FromMinutes(10), jobId: Guid.NewGuid());

// State-passing variant (zero closure heap allocations)
await jobQueue.EnqueueDelayedAsync(
    static async (userId, ct) => await ProcessUserReportAsync(userId, ct),
    state: userId,
    delay: TimeSpan.FromHours(1),
    jobId: Guid.NewGuid());
```

---

### Automatic Retries

Configure default retry behaviour globally in `appsettings.json`:

```json
{
  "Jobs": {
    "DefaultMaxRetries": 3,
    "DefaultRetryBackoff": "00:00:05",
    "DefaultRetryExponential": true
  }
}
```

- `DefaultMaxRetries`: Maximum number of retry attempts per failed job (default: `0`).
- `DefaultRetryBackoff`: Base delay between retries (default: `00:00:00` = immediate).
- `DefaultRetryExponential`: If `true`, uses exponential backoff `(2^attempt * backoff)`, capped at 30 seconds.

Zero-backoff retries loop inline within the worker without a channel round-trip, keeping retry overhead at near zero. Backoff retries are re-scheduled through the `DelayedJobScheduler`.

---

### Lifecycle Event Hooks

Subscribe to job lifecycle events on the `IBackgroundJobQueue` instance:

```csharp
var queue = serviceProvider.GetRequiredService<IBackgroundJobQueue>();

queue.OnJobEnqueued += jobId => logger.LogDebug("Job {JobId} enqueued", jobId);
queue.OnJobStarted  += jobId => logger.LogDebug("Job {JobId} started", jobId);
queue.OnJobCompleted += jobId => metrics.IncrementJobsCompleted();
queue.OnJobFailed   += (jobId, ex) => alertService.NotifyFailure(jobId, ex);
```

---

### OpenTelemetry Diagnostics

The library exposes a native `ActivitySource` for tracing job execution with full OpenTelemetry compatibility:

```csharp
// Source name: "Jobs.Vector"
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("Jobs.Vector")
    .AddOtlpExporter()
    .Build();
```

Each job execution creates an `Activity` with:
- `job.id` tag set to the job's ID
- `exception.type` and `exception.message` tags on failure
- Activity status set to `Ok` on completion or `Error` on failure

---

## Performance & Benchmarks

The library is designed for low overhead and high concurrency. The following benchmark results were recorded on a .NET 8 runtime:

### 1. Throughput & Allocation Benchmarks
* **Standard Workload:** 100,000 sequential `EnqueueAsync` and `DequeueAsync` operations -> **~979,000 ops/sec**.
* **Numeric `long` Job IDs:** 100,000 `long` ID `EnqueueAsync` operations -> **~988,000 ops/sec**.
* **Zero-Closure State-Passing:** 100,000 state-passing `EnqueueAsync<TState>` operations -> **~909,000 ops/sec** (with 0 closure class allocations and 0 delegate wrapper allocations).
* **Zero-Allocation Status Polling:** Polling `GetStatus` on unchanged status entries returns cached references with **0 heap allocations**.

### 2. Concurrency & Stress Benchmark
* **Workload:** 5 concurrent enqueuers pushing 1,000 jobs each (5,000 total) processed by 4 concurrent background worker loops with channel draining.
* **Result:** **100% execution success** under load.
* **Job Execution Latency:**
  * **P50 (Median):** ~15.51 ms
  * **P95:** ~16.16 ms
  * **P99:** ~16.60 ms

---

## Technical Architecture

### 1. Bounded Backpressure via `System.Threading.Channels`
`BackgroundJobQueue` utilizes C# Channels configured in `BoundedChannelFullMode.Wait` mode. If the queue fills up to its maximum capacity, the enqueuing thread blocks and waits rather than letting the memory heap grow out of control.

### 2. Multi-Thread Worker Loops
On application startup, `BackgroundJobWorker` reads the configured worker count and spawns that number of independent `Task` execution loops. The workers poll the channel concurrently:
*   A thread-safe dequeue removes the job item.
*   A linked `CancellationTokenSource` is created merging the shutdown token with the per-job cancellation token.
*   The worker updates the status store to `Processing` and raises the `OnJobStarted` event.
*   The delegate is executed inside a structured try/catch block with OpenTelemetry tracing.
*   Once finished, the state changes to `Completed` (or `Failed` with the exception message).

### 3. Per-Job Cancellation via `ConcurrentDictionary<string, CancellationTokenSource>`
Every enqueued job registers a dedicated `CancellationTokenSource` in `BackgroundJobQueue._activeJobs`. The worker creates a linked token combining the shutdown token with this per-job token. Calling `CancelJob(jobId)` triggers the job's CTS, which propagates `OperationCanceledException` into the running delegate within one scheduler cycle. The CTS is disposed after the job completes, fails, or is cancelled.

### 4. Delayed Job Scheduler via `PriorityQueue<JobItem, DateTimeOffset>`
`DelayedJobScheduler` is a `BackgroundService` that maintains a time-sorted priority queue. Jobs submitted via `EnqueueDelayedAsync` are added to a thread-safe staging `ConcurrentQueue<DelayedJob>` and then transferred into the `PriorityQueue` on the scheduler's polling tick (every 50ms). When a job's target `DateTimeOffset` is reached, it is promoted to the active bounded channel for immediate worker pickup.

### 5. Automatic Retry with Inline Looping and Exponential Backoff
Failed jobs check `AttemptCount <= MaxRetries`. For zero-backoff retries, the worker loops `continue`s within `ProcessJobItemAsync` — avoiding a channel round-trip and associated allocation. For backoff retries, the job is rescheduled through `DelayedJobScheduler`. Backoff delay is calculated as `2^(attempt-1) × BackoffBase`, capped at 30 seconds.

### 6. Thread-Safe Status Store with TTL Pruning
`InMemoryJobStatusStore` uses a `ConcurrentDictionary` to cache job outcomes. To prevent memory leaks, jobs have an associated TTL retention window:
*   **Lazy Eviction:** Calling `GetStatus(jobId)` checks the timestamp; if the job has expired, it is removed immediately using atomic operations.
*   **Active Cleanup:** `JobStatusSweepWorker` runs a background task that sweeps the dictionary and removes expired entries periodically.

---

## License

Distributed under the PolyForm Noncommercial License 1.0.0. See [LICENSE](LICENSE) for details.
