# Jobs.Vector Wiki

Welcome to the **Jobs.Vector** wiki — a comprehensive reference for the library.

## Contents

| Page | Description |
|---|---|
| [Home](Home) | Overview and navigation |
| [Getting-Started](Getting-Started) | Installation and first job in 5 minutes |
| [Configuration](Configuration) | All configuration options explained |
| [Enqueueing-Jobs](Enqueueing-Jobs) | Standard, state-passing, Guid/long IDs |
| [Job-Status-and-Metadata](Job-Status-and-Metadata) | Polling status, typed metadata |
| [Cancellation](Cancellation) | Per-job and shutdown cancellation |
| [Delayed-and-Scheduled-Jobs](Delayed-and-Scheduled-Jobs) | Deferred execution with delay |
| [Retries-and-Backoff](Retries-and-Backoff) | Automatic retry with linear/exponential backoff |
| [Lifecycle-Events](Lifecycle-Events) | Enqueued, Started, Completed, Failed events |
| [OpenTelemetry-Diagnostics](OpenTelemetry-Diagnostics) | ActivitySource tracing integration |
| [Dependency-Injection](Dependency-Injection) | DI registration and service resolution |
| [Without-DI](Without-DI) | Direct instantiation without DI |
| [Architecture](Architecture) | Technical design and internals |
| [Performance](Performance) | Benchmarks and allocation profile |
| [API-Reference](API-Reference) | All public types and members |
| [Contributing](Contributing) | Build, test, and release instructions |

---

## What is Jobs.Vector?

**Jobs.Vector** is a portable **.NET 8** background job queue and worker hosting service built entirely on standard .NET primitives — no databases, no Redis, no external dependencies.

```
dotnet add package Jobs.Vector
```

### Core capabilities

- ⚡ **Bounded backpressure** via `System.Threading.Channels`
- 👯 **Multi-threaded execution** with configurable concurrent worker loops
- ❌ **Per-job cancellation** — cancel any job by ID at any point
- ⏰ **Delayed / scheduled jobs** — enqueue jobs to run after a delay
- 🔄 **Automatic retries** with linear or exponential backoff
- 🪝 **Lifecycle event hooks** — Enqueued, Started, Completed, Failed
- 📡 **OpenTelemetry diagnostics** — native `ActivitySource` tracing
- 🔒 **Thread-safe status store** with configurable TTL eviction
- 💉 **DI-friendly** via `AddBackgroundJobs()`

---

## Quick Navigation

**New here?** → Start with [Getting Started](Getting-Started)  
**Configuring?** → See [Configuration](Configuration)  
**Want retries?** → See [Retries and Backoff](Retries-and-Backoff)  
**Monitoring?** → See [OpenTelemetry Diagnostics](OpenTelemetry-Diagnostics)  
**Looking up types?** → See [API Reference](API-Reference)
