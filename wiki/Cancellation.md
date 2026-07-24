# Cancellation

Jobs.Vector supports two kinds of cancellation:

| Kind | Trigger | Effect |
|---|---|---|
| **Per-job** | `jobQueue.CancelJob(jobId)` | Cancels one specific job immediately. |
| **Shutdown** | Application stopping (SIGTERM / `Ctrl+C`) | Cancels all in-flight jobs gracefully. |

---

## Per-Job Cancellation

### How It Works

Every enqueued job gets a dedicated `CancellationTokenSource` registered in the queue. When you call `CancelJob`, that CTS is cancelled, which propagates into the **linked token** passed to your job delegate — causing any `await` inside the delegate to throw `OperationCanceledException`.

```
jobQueue.CancelJob("my-job")
    → _activeJobs["my-job"].Cancel()
    → linked jobToken.IsCancellationRequested = true
    → delegate's ct throws OperationCanceledException
    → worker sets status to Failed with "Cancelled by request."
```

### Calling CancelJob

```csharp
// String ID
bool wasCancelled = jobQueue.CancelJob("my-job-001");

// Guid ID (extension method)
bool wasCancelled = jobQueue.CancelJob(guidJobId);

// Long ID (extension method)
bool wasCancelled = jobQueue.CancelJob(longJobId);
```

Returns `true` if the job was found and cancellation was triggered, `false` if the job ID is unknown (already completed, already failed, or never enqueued).

### Example — Cancel After a Timeout

```csharp
var jobId = Guid.NewGuid();
await jobQueue.EnqueueAsync(async ct =>
{
    await DoLongWorkAsync(ct); // Will throw on cancel
}, jobId);

// Cancel the job after 30 seconds if still running
_ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
{
    jobQueue.CancelJob(jobId);
});
```

### What Happens to the Status

Cancelled jobs are marked:

```json
{
  "status": "Failed",
  "error": "Cancelled by request."
}
```

Cancelled jobs are **not retried** even if `DefaultMaxRetries > 0`.

---

## Shutdown Cancellation

When the ASP.NET Core host stops (SIGTERM, `Ctrl+C`, or `IHost.StopAsync()`), the `BackgroundJobWorker` receives a `stoppingToken` that is cancelled.

- Any in-flight job delegate receives `OperationCanceledException` via the linked token.
- The worker sets status to `Failed` with error `"Cancelled by shutdown."`.
- The worker loop exits cleanly.
- **No retries** are performed for shutdown-cancelled jobs.

Shutdown status:

```json
{
  "status": "Failed",
  "error": "Cancelled by shutdown."
}
```

### Distinguishing Shutdown vs. Per-Job Cancellation

The worker checks the **stoppingToken first** (shutdown takes priority), then the per-job token. This means if both are cancelled simultaneously, the job is recorded as a shutdown cancellation.

---

## Writing Cancellation-Aware Job Delegates

Your delegate receives a `CancellationToken` that combines both cancellation sources. Pass it to every awaitable call:

```csharp
await jobQueue.EnqueueAsync(async ct =>
{
    // Pass ct to all awaitable operations:
    await httpClient.GetAsync(url, ct);
    await dbContext.SaveChangesAsync(ct);
    await Task.Delay(1000, ct);
    
    // For CPU-bound work, check periodically:
    for (int i = 0; i < items.Count; i++)
    {
        ct.ThrowIfCancellationRequested();
        Process(items[i]);
    }
}, jobId);
```

---

## CTS Lifecycle

- A `CancellationTokenSource` is created for each job on `EnqueueAsync`.
- It is stored in `BackgroundJobQueue._activeJobs`.
- It is **disposed and removed** when the job completes, fails, or is cancelled.
- Delayed-retry re-enqueues reuse the same CTS (the original one remains registered until final completion/failure).
