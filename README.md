# Jobs.Vector

A portable **.NET 8** background job queue and worker hosting service built on standard .NET primitives (`System.Threading.Channels` and `BackgroundService`).

It processes CPU-intensive and long-running operations asynchronously using bounded channels for backpressure, supports multi-threaded concurrent execution loops, and offers a thread-safe in-memory job status store with configurable time-to-live (TTL) eviction.

[![CI](https://github.com/tafallen/jobs-vector/actions/workflows/ci.yml/badge.svg)](https://github.com/tafallen/jobs-vector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Jobs.Vector)](https://www.nuget.org/packages/Jobs.Vector)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)

---

## Features

- ⚡ **Bounded backpressure** — utilizes `System.Threading.Channels` with bounded capacity to block the enqueuing thread and prevent uncontrolled heap growth
- 👯 **Multi-threaded execution loops** — spawns configurable multiple concurrent background Task execution loops polling the channel
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
    "SweepInterval": "00:01:00"
  }
}
```

### Enqueuing a Job

Inject `IBackgroundJobQueue` and enqueue an asynchronous task closure:
```csharp
public class IngestionController(IBackgroundJobQueue jobQueue, IJobStatusStore statusStore)
{
    public async Task StartIngestionAsync(string fileId, CancellationToken ct)
    {
        string jobId = Guid.NewGuid().ToString();

        // Enqueue job delegate containing processing task context
        await jobQueue.EnqueueAsync(async (jobCt) =>
        {
            // Simulated work logic
            await Task.Delay(5000, jobCt);
            
            // Set metadata on the status store
            statusStore.SetMetadata(jobId, new Dictionary<string, object>
            {
                ["fileId"] = fileId,
                ["peopleImported"] = 42,
                ["eventsImported"] = 99
            });
        }, jobId, ct);
    }
}
```

### Polling Job Status

Query the status store using the unique `JobId`:
```csharp
public JobStatusSnapshot? GetJobStatus(string jobId)
{
    return statusStore.GetStatus(jobId);
}
```

### Without DI (direct use)

```csharp
using Microsoft.Extensions.Options;
using Jobs.Vector;

var options = Options.Create(new JobsOptions
{
    Workers = 1,
    QueueCapacity = 10,
    StatusRetention = TimeSpan.FromMinutes(5)
});

var timeProvider = TimeProvider.System;
var statusStore = new InMemoryJobStatusStore(timeProvider, options);
var jobQueue = new BackgroundJobQueue(statusStore, options);

// Enqueue a job directly
string jobId = Guid.NewGuid().ToString();
await jobQueue.EnqueueAsync(async (ct) =>
{
    await Task.Delay(1000, ct);
}, jobId);
```

---

## Performance & Benchmarks

The library is designed for low overhead and high concurrency. The following benchmark results were recorded on a .NET 8 runtime:

### 1. Throughput Benchmark
* **Standard Workload:** 100,000 sequential `EnqueueAsync` and `DequeueAsync` operations -> **~917,000 ops/sec**.
* **Zero-Closure State-Passing:** 100,000 state-passing `EnqueueAsync<TState>` operations -> **~779,000 ops/sec** (with zero hidden C# closure heap allocations).
* **Impact:** High-frequency scheduling executes with minimal CPU and zero closure allocation pressure.

### 2. Concurrency & Stress Benchmark
* **Workload:** 5 concurrent enqueuers pushing 1,000 jobs each (5,000 total) processed by 4 concurrent background worker loops with channel draining.
* **Result:** **100% execution success** under load.
* **Job Execution Latency:**
  * **P50 (Median):** ~15.50 ms
  * **P95:** ~16.11 ms
  * **P99:** ~16.49 ms

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
*   **Lazy Eviction:** Calling `GetStatus(jobId)` checks the timestamp; if the job has expired, it is removed immediately using atomic operations.
*   **Active Cleanup:** `JobStatusSweepWorker` runs a background task that sweeps the dictionary and removes expired entries periodically.

---

## License

Distributed under the PolyForm Noncommercial License 1.0.0. See [LICENSE](LICENSE) for details.
