# Getting Started

Get up and running with Jobs.Vector in under 5 minutes.

## Prerequisites

- .NET 8 SDK or later
- An ASP.NET Core or Worker Service project

## 1. Install the Package

```bash
dotnet add package Jobs.Vector
```

## 2. Register Services

In your `Program.cs` (or `Startup.cs`), call `AddBackgroundJobs()`:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBackgroundJobs(builder.Configuration);

var app = builder.Build();
app.Run();
```

## 3. Add Configuration

Add a `Jobs` section to your `appsettings.json`:

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

See [Configuration](Configuration) for all available settings.

## 4. Enqueue Your First Job

Inject `IBackgroundJobQueue` and `IJobStatusStore` into your controller or service:

```csharp
public class ReportController(
    IBackgroundJobQueue jobQueue,
    IJobStatusStore statusStore) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateReport(
        [FromBody] ReportRequest request,
        CancellationToken ct)
    {
        string jobId = Guid.NewGuid().ToString();

        await jobQueue.EnqueueAsync(async jobCt =>
        {
            // Your long-running work here
            var result = await GenerateReportAsync(request, jobCt);
            statusStore.SetMetadata(jobId, "reportUrl", result.Url);
        }, jobId, ct);

        return Accepted(new { jobId });
    }

    [HttpGet("{jobId}")]
    public IActionResult GetStatus(string jobId)
    {
        var snapshot = statusStore.GetStatus(jobId);
        if (snapshot is null) return NotFound();
        return Ok(snapshot);
    }
}
```

## 5. Poll for Completion

Your client polls the status endpoint using the `jobId` returned from the POST:

```
GET /report/{jobId}
```

The response is a `JobStatusSnapshot`:

```json
{
  "status": "Completed",
  "progress": 100,
  "error": null,
  "metadata": {
    "reportUrl": "https://...",
    "durationMs": 1234.5
  }
}
```

Possible status values: `Queued`, `Processing`, `Completed`, `Failed`.

## What's Next?

- [Configuration](Configuration) — tune capacity, workers, retention, retries
- [Enqueueing Jobs](Enqueueing-Jobs) — state-passing, Guid/long IDs, zero-allocation patterns
- [Retries and Backoff](Retries-and-Backoff) — automatic transient failure handling
- [Delayed Jobs](Delayed-and-Scheduled-Jobs) — schedule jobs to run after a delay
- [Cancellation](Cancellation) — cancel running jobs on demand
