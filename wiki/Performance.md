# Performance

Jobs.Vector is designed for low overhead, high throughput, and minimal allocations on the hot path.

## Benchmark Results

All benchmarks run on .NET 8, debug configuration, Windows 11 / AMD CPU.

### Throughput — `EnqueueAsync` / `DequeueAsync`

| Test | Ops | Time | Throughput |
|---|---|---|---|
| Standard closure | 100,000 | ~101 ms | **~990,000 ops/sec** |
| State-passing (zero-closure) | 100,000 | ~104 ms | **~962,000 ops/sec** |
| Long job ID | 100,000 | ~89 ms | **~1,124,000 ops/sec** |

### Concurrency & Stress

4 workers, 5,000 jobs with simulated 15ms I/O:

| Metric | Value |
|---|---|
| Total throughput | 5,000 / 5,000 jobs (100%) |
| Total wall time | ~19.4 s |
| P50 latency | ~15.51 ms |
| P95 latency | ~16.20 ms |
| P99 latency | ~16.62 ms |

The tight P50/P99 spread (1.11 ms) indicates consistent, low-jitter scheduling.

---

## Allocation Profile

### Hot Path (per enqueue)

| Allocation | Source |
|---|---|
| `JobItem` (1 object) | New item per enqueue |
| `CancellationTokenSource` (1 object) | Per-job CTS |
| Closure object (0 if state-passing) | Lambda capture |

### State-Passing Overloads

Using `EnqueueAsync<TState>(Func<TState, CancellationToken, Task>, TState, ...)`:
- The `static` lambda captures nothing — **zero closure allocation**.
- `TState` is value-type friendly (no boxing for struct state).

### Status Store

- `GetStatus` is allocation-free for unmodified entries (cached `JobStatusSnapshot`).
- `SetStatus` / `SetMetadata` allocate a new `JobStatusSnapshot` (cached until next write).

### Worker Loop

- `DequeueAsync` allocates a `ValueTask<JobItem>` (stack-allocated in the common fast path).
- `ProcessJobItemAsync` is a `ValueTask<bool>` — avoids `Task` allocation in the common synchronous return path.
- `ActivitySource.StartActivity` returns `null` when no listener is registered — **zero OTel overhead** in production without a tracer.

---

## Channel Configuration

The channel uses `System.Threading.Channels.Channel.CreateBounded<JobItem>`:

```csharp
var options = new BoundedChannelOptions(capacity)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleWriter = false,
    SingleReader = workerCount == 1   // Single-reader optimization when Workers=1
};
```

Single-reader mode allows the channel to use a more efficient internal implementation when there is exactly one consuming loop.

---

## Worker Loop Design

Workers use a two-phase dequeue pattern:

1. `await DequeueAsync(stoppingToken)` — async wait for first item.
2. `while (TryDequeue(out item))` — synchronous drain while items are available.

This minimises the number of async state machine transitions and reduces scheduler overhead when jobs arrive in bursts.

---

## Retry Performance

| Retry Mode | Overhead per retry |
|---|---|
| Zero-backoff (inline loop) | ~0 allocation, ~1 µs overhead |
| Backoff (via DelayedJobScheduler) | 1 PriorityQueue.Enqueue, ~O(log n) |

Zero-backoff retries `continue` within the existing worker loop — no channel write, no dequeue, no extra state machine suspension.

---

## Tuning Recommendations

| Goal | Setting |
|---|---|
| Maximum throughput | `Workers = Environment.ProcessorCount` |
| Minimize memory | `QueueCapacity = 50–100`, `StatusRetention = 00:15:00` |
| Handle burst traffic | `QueueCapacity = 1000–5000` |
| Reduce GC pressure | Use state-passing overloads (`static` lambdas) |
| Fast retries | `DefaultRetryBackoff = 00:00:00` (inline loop) |
| Rate-limited external calls | `Workers = 1–2`, `DefaultRetryBackoff = 00:00:30` exponential |
