# Configuration

All Jobs.Vector settings are bound from the `Jobs` section of your `appsettings.json`.

## Full Configuration Example

```json
{
  "Jobs": {
    "Workers": 2,
    "QueueCapacity": 100,
    "StatusRetention": "01:00:00",
    "SweepInterval": "00:15:00",
    "DefaultMaxRetries": 3,
    "DefaultRetryBackoff": "00:00:05",
    "DefaultRetryExponential": true
  }
}
```

---

## Options Reference

### `Workers`

| Property | Type | Default |
|---|---|---|
| `Workers` | `int` | `1` |

The number of concurrent background worker execution loops to spawn on startup.

- A value of `1` uses a single-reader optimized channel for lower overhead.
- Increase this to process multiple jobs in parallel.

```json
{ "Jobs": { "Workers": 4 } }
```

> **Tip:** Set `Workers` to `Environment.ProcessorCount` for CPU-intensive jobs.

---

### `QueueCapacity`

| Property | Type | Default |
|---|---|---|
| `QueueCapacity` | `int` | `100` |

The maximum number of jobs that can be waiting in the channel at once. When full, `EnqueueAsync` blocks the calling thread (backpressure) rather than growing memory unboundedly.

```json
{ "Jobs": { "QueueCapacity": 500 } }
```

> **Tip:** For high-throughput APIs, increase `QueueCapacity` to absorb bursts.

---

### `StatusRetention`

| Property | Type | Default |
|---|---|---|
| `StatusRetention` | `TimeSpan` | `01:00:00` |

How long a completed, failed, or queued job's status remains queryable in the in-memory store after its last update before it is evicted. Formatted as `HH:mm:ss`.

```json
{ "Jobs": { "StatusRetention": "00:30:00" } }
```

> **Warning:** Setting this too low may cause status entries to expire before clients have polled them.

---

### `SweepInterval`

| Property | Type | Default |
|---|---|---|
| `SweepInterval` | `TimeSpan` | `00:15:00` |

How often `JobStatusSweepWorker` actively scans the status store and evicts expired entries. In addition to this active sweep, lazy eviction occurs on every `GetStatus` call.

```json
{ "Jobs": { "SweepInterval": "00:05:00" } }
```

---

### `DefaultMaxRetries`

| Property | Type | Default |
|---|---|---|
| `DefaultMaxRetries` | `int` | `0` |

The default maximum number of automatic retry attempts for failed jobs. `0` means no retries — failed jobs are immediately marked `Failed`.

```json
{ "Jobs": { "DefaultMaxRetries": 3 } }
```

See [Retries and Backoff](Retries-and-Backoff) for full details.

---

### `DefaultRetryBackoff`

| Property | Type | Default |
|---|---|---|
| `DefaultRetryBackoff` | `TimeSpan` | `00:00:00` |

The base delay between retry attempts. Formatted as `HH:mm:ss`.

- `00:00:00` — immediate retry (loops inline, zero overhead).
- Any positive value — delayed retry via `DelayedJobScheduler`.

```json
{ "Jobs": { "DefaultRetryBackoff": "00:00:05" } }
```

---

### `DefaultRetryExponential`

| Property | Type | Default |
|---|---|---|
| `DefaultRetryExponential` | `bool` | `false` |

When `true`, uses exponential backoff: `delay = 2^(attempt-1) × DefaultRetryBackoff`, capped at 30 seconds.

| Attempt | Linear (5s) | Exponential (5s base) |
|---|---|---|
| 1st retry | 5s | 5s |
| 2nd retry | 5s | 10s |
| 3rd retry | 5s | 20s |
| 4th retry | 5s | 30s (capped) |

```json
{
  "Jobs": {
    "DefaultMaxRetries": 5,
    "DefaultRetryBackoff": "00:00:05",
    "DefaultRetryExponential": true
  }
}
```

---

## Validation on Startup

All options are validated at startup using `ValidateOnStart()`. Invalid configuration throws an exception before the application finishes bootstrapping:

- `Workers` must be ≥ 1
- `StatusRetention` must be > `TimeSpan.Zero`
- `QueueCapacity` must be ≥ 1
- `SweepInterval` must be > `TimeSpan.Zero`

---

## Programmatic Configuration

You can configure options in code instead of `appsettings.json`:

```csharp
builder.Services.Configure<JobsOptions>(options =>
{
    options.Workers = Environment.ProcessorCount;
    options.QueueCapacity = 500;
    options.StatusRetention = TimeSpan.FromHours(2);
    options.DefaultMaxRetries = 3;
    options.DefaultRetryBackoff = TimeSpan.FromSeconds(5);
    options.DefaultRetryExponential = true;
});
builder.Services.AddBackgroundJobs(builder.Configuration);
```
