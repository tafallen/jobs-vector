# Retries and Backoff

Jobs.Vector supports automatic retry of failed jobs with configurable linear or exponential backoff.

## Configuration

Configure retries globally in `appsettings.json`:

```json
{
  "Jobs": {
    "DefaultMaxRetries": 3,
    "DefaultRetryBackoff": "00:00:05",
    "DefaultRetryExponential": true
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `DefaultMaxRetries` | `0` | Maximum number of retries per job. `0` = no retries. |
| `DefaultRetryBackoff` | `00:00:00` | Base delay between retries. |
| `DefaultRetryExponential` | `false` | If `true`, uses exponential backoff. |

---

## How Retries Work

When a job delegate throws an unhandled exception:

1. `AttemptCount` is incremented.
2. If `AttemptCount <= MaxRetries` and the service is not stopping:
   - Status is set to `Queued` with the retry reason in `error`.
   - The job is re-queued (immediately or after a backoff delay).
3. If `AttemptCount > MaxRetries`:
   - Status is set to `Failed` with the final exception message.
   - `OnJobFailed` event is raised.

### Attempt Count Tracking

The retry count is tracked on the `JobItem` object itself — the same heap object is used across all attempts. `AttemptCount` starts at `0` and is incremented before each check, so:

| Attempt # | AttemptCount | Retried? (MaxRetries=3) |
|---|---|---|
| 1st (original) | 1 | Yes, 1 ≤ 3 |
| 2nd | 2 | Yes, 2 ≤ 3 |
| 3rd | 3 | Yes, 3 ≤ 3 |
| 4th | 4 | **No**, 4 > 3 → Failed |

So `MaxRetries = 3` means **4 total attempts** (1 original + 3 retries).

---

## Zero-Backoff Retries (Inline)

When `DefaultRetryBackoff` is `00:00:00` (the default), retries loop **inline** within the worker loop — no channel round-trip, no allocation:

```
Job throws → AttemptCount++ → AttemptCount <= MaxRetries? → continue (loop in-place)
```

This is the lowest-overhead retry strategy, ideal for transient in-memory failures.

---

## Backoff Retries (Delayed)

When `DefaultRetryBackoff > TimeSpan.Zero`, the retry is scheduled through `DelayedJobScheduler`:

```
Job throws → backoff calculated → ScheduleRetry(item, backoff)
           → job re-enters PriorityQueue at now + backoff
           → after delay, promoted to active channel
```

### Linear Backoff

All retries wait the same fixed duration:

```
Attempt 1 fails → wait 5s → attempt 2
Attempt 2 fails → wait 5s → attempt 3
Attempt 3 fails → wait 5s → attempt 4
```

### Exponential Backoff

Each retry doubles the wait, capped at 30 seconds:

```
delay = min(2^(attemptCount-1) × backoffBase, 30s)
```

| Attempt | AttemptCount | Delay (5s base) |
|---|---|---|
| Retry 1 | 1 | 2^0 × 5s = **5s** |
| Retry 2 | 2 | 2^1 × 5s = **10s** |
| Retry 3 | 3 | 2^2 × 5s = **20s** |
| Retry 4 | 4 | 2^3 × 5s = 40s → **30s** (capped) |

---

## Status During Retries

While waiting for a retry, the job's status is set to `Queued` with the retry count and error message in the `error` field:

```json
{
  "status": "Queued",
  "error": "Retry 1/3: Connection timeout."
}
```

After all retries are exhausted:

```json
{
  "status": "Failed",
  "error": "System.Net.Http.HttpRequestException: Connection timeout.\n..."
}
```

---

## Retries and Cancellation

Retries are **not performed** if:

- The job was explicitly cancelled via `CancelJob`.
- The application is shutting down (`stoppingToken.IsCancellationRequested`).

In both cases the job goes directly to `Failed`.

---

## Per-Job Retry Override (Coming Soon)

Currently `MaxRetries`, `RetryBackoff`, and `RetryExponential` are set globally via `JobsOptions` and applied at enqueue time. Per-job overrides are a planned future feature.

---

## Recommended Retry Strategies

| Scenario | MaxRetries | Backoff | Exponential |
|---|---|---|---|
| Transient DB failures | 3–5 | 0s | No (fast inline) |
| External API calls | 3 | 2s | Yes |
| Email delivery | 5 | 30s | No |
| Idempotent batch jobs | 10 | 5s | Yes |
| Critical single-shot jobs | 0 | — | — |
