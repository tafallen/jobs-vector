# Contributing

Thank you for your interest in contributing to Jobs.Vector! This page covers how to build, test, and release the library.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Git
- A GitHub account with access to the repository

---

## Building

```bash
git clone https://github.com/tafallen/jobs-vector.git
cd jobs-vector
dotnet build
```

The solution contains two projects:

| Project | Path | Description |
|---|---|---|
| `Jobs.Vector` | `src/Jobs.Vector/` | The library |
| `Jobs.Vector.Tests` | `tests/Jobs.Vector.Tests/` | xUnit test suite |

---

## Running Tests

```bash
# All tests
dotnet test

# Verbose output
dotnet test --logger "console;verbosity=detailed"

# Specific test class
dotnet test --filter "BackgroundJobWorkerTests"

# Performance benchmarks only
dotnet test --filter "PerformanceTests"
```

### Test Categories

| Filter | Description |
|---|---|
| `BackgroundJobQueueTests` | Channel enqueue/dequeue, backpressure |
| `BackgroundJobWorkerTests` | Worker lifecycle, failure handling, cancellation |
| `InMemoryJobStatusStoreTests` | Status store, TTL eviction, metadata |
| `JobStatusSweepWorkerTests` | Active sweep worker |
| `CancellationTests` | Per-job cancellation, shutdown cancellation |
| `RetryTests` | Retry counting, inline vs. backoff retry |
| `LifecycleEventTests` | Event hooks |
| `DelayedJobTests` | Delayed scheduling |
| `OpenTelemetryTests` | ActivitySource, tag assertions |
| `PerformanceTests` | Throughput and concurrency benchmarks |

---

## Code Style

- C# 12 / .NET 8 idiomatic code
- Prefer `ValueTask` over `Task` for queue methods (avoids heap allocation in the synchronous fast path)
- Use `static` lambdas where closures are not needed
- All public API members must have XML `<summary>` documentation
- Prefer `readonly` fields and `init`-only properties for immutable data

---

## Testing Philosophy

This project follows **Test-Driven Development (TDD)**:

1. Write a failing test that describes the expected behaviour.
2. Write the minimal implementation to make it pass.
3. Refactor for clarity and performance.

All new features **must** have accompanying unit tests. Performance-sensitive changes should include a benchmark comparison.

---

## Submitting a Pull Request

1. Fork the repository and create a feature branch:
   ```bash
   git checkout -b feature/my-feature
   ```

2. Make your changes with tests.

3. Ensure all tests pass:
   ```bash
   dotnet test
   ```

4. Ensure the build is clean with no warnings:
   ```bash
   dotnet build -warnaserror
   ```

5. Push and open a Pull Request against `main`.

---

## Releasing a New Version

### 1. Update the Version

Edit `src/Jobs.Vector/Jobs.Vector.csproj` and bump `<Version>`:

```xml
<Version>1.3.0</Version>
```

### 2. Update the Changelog / Release Notes

Update `CHANGELOG.md` (or create one if it doesn't exist) summarising changes since the last release.

### 3. Create and Push a Git Tag

```bash
git tag v1.3.0
git push origin v1.3.0
```

The CI pipeline (`ci.yml`) triggers on tags prefixed with `v` and:
- Builds and tests the library
- Packs the NuGet package (`dotnet pack`)
- Publishes to NuGet.org

### 4. Create a GitHub Release

Go to **Releases → Draft a new release**, select the tag, and add release notes.

---

## CI Pipeline

The GitHub Actions workflow at `.github/workflows/ci.yml` runs on every push and pull request to `main`:

1. `dotnet restore`
2. `dotnet build --no-restore`
3. `dotnet test --no-build`
4. `dotnet pack` (on tag pushes only)
5. `dotnet nuget push` (on tag pushes, requires `NUGET_API_KEY` secret)

---

## Documentation

- Source code XML docs are the authoritative API documentation.
- `README.md` is the library's front page — keep it accurate and concise.
- `docs/` contains architectural design documents.
- This wiki is the comprehensive user guide.

When adding or changing a feature, update **all four** of:
1. XML docs in the source
2. `README.md` (Features list and relevant sections)
3. This wiki
4. `docs/` if the change involves architecture decisions
