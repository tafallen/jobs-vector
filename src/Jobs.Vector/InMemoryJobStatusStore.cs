using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

public class InMemoryJobStatusStore : IJobStatusStore
{
    private static readonly IReadOnlyDictionary<string, object> EmptyMetadata = new Dictionary<string, object>();

    private sealed record Entry(JobStatusSnapshot Snapshot, DateTimeOffset UpdatedAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;

    public InMemoryJobStatusStore(TimeProvider timeProvider, IOptions<JobsOptions> options)
    {
        _timeProvider = timeProvider;
        _retention = options.Value.StatusRetention;
    }

    // Convenience constructor for unit tests that do not exercise TTL: system clock,
    // default retention window.
    public InMemoryJobStatusStore()
        : this(TimeProvider.System, Options.Create(new JobsOptions()))
    {
    }

    public void SetStatus(string jobId, JobStatus status, int progress = 0, string? error = null)
    {
        _entries.AddOrUpdate(
            jobId,
            _ => new Entry(new JobStatusSnapshot(status, progress, error, EmptyMetadata), _timeProvider.GetUtcNow()),
            (_, existing) => new Entry(
                existing.Snapshot with { Status = status, Progress = progress, Error = error },
                _timeProvider.GetUtcNow()));
    }

    public void SetMetadata(string jobId, IReadOnlyDictionary<string, object> metadata)
    {
        _entries.AddOrUpdate(
            jobId,
            _ => new Entry(
                new JobStatusSnapshot(JobStatus.Processing, 0, null, new Dictionary<string, object>(metadata)),
                _timeProvider.GetUtcNow()),
            (_, existing) => new Entry(
                existing.Snapshot with { Metadata = Merge(existing.Snapshot.Metadata, metadata) },
                _timeProvider.GetUtcNow()));
    }

    private static IReadOnlyDictionary<string, object> Merge(
        IReadOnlyDictionary<string, object> existing, IReadOnlyDictionary<string, object> updates)
    {
        var merged = new Dictionary<string, object>(existing);
        foreach (var (key, value) in updates)
        {
            merged[key] = value;
        }
        return merged;
    }

    public JobStatusSnapshot? GetStatus(string jobId)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
        {
            return null;
        }

        if (_timeProvider.GetUtcNow() - entry.UpdatedAt > _retention)
        {
            _entries.TryRemove(jobId, out _);
            return null;
        }

        return entry.Snapshot;
    }

    public void PruneExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (jobId, entry) in _entries)
        {
            if (now - entry.UpdatedAt > _retention)
            {
                _entries.TryRemove(jobId, out _);
            }
        }
    }

    // Test/diagnostic-only: not part of IJobStatusStore, exposes the live entry count without
    // triggering GetStatus's own lazy eviction (which would make a "did PruneExpired do this"
    // test ambiguous).
    public int EntryCount => _entries.Count;
}
