# OpenTelemetry Diagnostics

Jobs.Vector ships native OpenTelemetry support via `System.Diagnostics.ActivitySource`. No additional packages are required on the library side — just configure your OTel pipeline to listen to the source.

## Activity Source Name

```
Jobs.Vector
```

---

## Quick Setup

Add the `Jobs.Vector` source to your `TracerProvider`:

```csharp
// In Program.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Jobs.Vector")        // Listen to Jobs.Vector traces
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());             // Or AddJaegerExporter(), etc.
```

---

## Activities Created

One activity is created per job execution attempt under the name `"job.execute"`:

| Property | Value |
|---|---|
| Operation Name | `job.execute` |
| Kind | `Internal` |
| Status on success | `Ok` |
| Status on failure | `Error` (with error description) |

---

## Tags

| Tag | Type | Description |
|---|---|---|
| `job.id` | `string` | The job's string ID. |
| `exception.type` | `string` | Full type name of the thrown exception (on failure). |
| `exception.message` | `string` | The exception message (on failure). |

---

## Viewing Traces

### Jaeger

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Jobs.Vector")
        .AddJaegerExporter(o =>
        {
            o.AgentHost = "localhost";
            o.AgentPort = 6831;
        }));
```

### Zipkin

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Jobs.Vector")
        .AddZipkinExporter(o =>
        {
            o.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
        }));
```

### Console (for development)

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Jobs.Vector")
        .AddConsoleExporter());
```

---

## Activity Lifecycle

```
Worker dequeues job
    → ActivitySource.StartActivity("job.execute", Internal, tags: { job.id })
    → SetStatus(Processing)
    → Execute delegate
        → SUCCESS: activity.SetStatus(Ok) → SetStatus(Completed)
        → FAILURE: activity.SetStatus(Error) + tags + retry or final fail
    → activity.Dispose() (on each attempt for retried jobs)
```

Each **attempt** gets its own `Activity` with its own start/stop timestamp. For retried jobs, you will see multiple `job.execute` spans with the same `job.id`.

---

## Correlating with Incoming HTTP Requests

If you want the background job span to be a **child** of the HTTP request that enqueued it, capture the current `ActivityContext` at enqueue time and inject it into your job:

```csharp
// In your controller:
var parentContext = Activity.Current?.Context ?? default;
var jobId = Guid.NewGuid();

await jobQueue.EnqueueAsync(async ct =>
{
    // Manually start a child activity linked to the parent context
    using var activity = new ActivitySource("Jobs.Vector")
        .StartActivity("job.execute", ActivityKind.Internal, parentContext);
    
    await DoWorkAsync(ct);
}, jobId);
```

> **Note:** The built-in activity is started with `default` parent context (no automatic propagation). Explicit parent linking as shown above is required for distributed trace correlation.

---

## Custom Activity Listener (Testing)

In unit tests, you can listen to activities without an OTel SDK:

```csharp
Activity? captured = null;
using var listener = new ActivityListener
{
    ShouldListenTo = src => src.Name == "Jobs.Vector",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured = activity
};
ActivitySource.AddActivityListener(listener);

// Run your job...
await worker.StartAsync(CancellationToken.None);
await WaitForCompletionAsync(statusStore, jobId);

Assert.Equal("job.execute", captured!.OperationName);
Assert.Equal(jobId, captured.GetTagItem("job.id"));
```

---

## No-Op When Not Listening

`ActivitySource.StartActivity` returns `null` when no listener is configured (no OTel SDK or listener registered). The library code uses `activity?.SetStatus(...)` throughout, so there is **zero overhead** when tracing is disabled.
