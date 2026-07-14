using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// An in-memory, thread-safe implementation of <see cref="IJobStatusStore"/> backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
public class InMemoryJobStatusStore : IJobStatusStore
{
    private static readonly IReadOnlyDictionary<string, object> EmptyMetadata = new Dictionary<string, object>();

    private sealed record Entry(JobStatusSnapshot Snapshot, DateTimeOffset UpdatedAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryJobStatusStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The provider used to obtain the current time.</param>
    /// <param name="options">The configuration options for background jobs.</param>
    public InMemoryJobStatusStore(TimeProvider timeProvider, IOptions<JobsOptions> options)
    {
        _timeProvider = timeProvider;
        _retention = options.Value.StatusRetention;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryJobStatusStore"/> class with system defaults (primarily for unit tests).
    /// </summary>
    public InMemoryJobStatusStore()
        : this(TimeProvider.System, Options.Create(new JobsOptions()))
    {
    }

    /// <inheritdoc />
    public void SetStatus(string jobId, JobStatus status, int progress = 0, string? error = null)
    {
        _entries.AddOrUpdate(
            jobId,
            _ => new Entry(new JobStatusSnapshot(status, progress, error, EmptyMetadata), _timeProvider.GetUtcNow()),
            (_, existing) => new Entry(
                existing.Snapshot with { Status = status, Progress = progress, Error = error },
                _timeProvider.GetUtcNow()));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public JobStatusSnapshot? GetStatus(string jobId)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
        {
            return null;
        }

        if (_timeProvider.GetUtcNow() - entry.UpdatedAt > _retention)
        {
            // Use the TryRemove(KeyValuePair) overload to avoid removing a newly updated entry for the same jobId
            _entries.TryRemove(KeyValuePair.Create(jobId, entry));
            return null;
        }

        return entry.Snapshot;
    }

    /// <inheritdoc />
    public void PruneExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (jobId, entry) in _entries)
        {
            if (now - entry.UpdatedAt > _retention)
            {
                // Use the TryRemove(KeyValuePair) overload to avoid removing a newly updated entry for the same jobId
                _entries.TryRemove(KeyValuePair.Create(jobId, entry));
            }
        }
    }

    /// <summary>
    /// Gets the count of active status entries currently in the store. Primarily for diagnostic or testing purposes.
    /// </summary>
    public int EntryCount => _entries.Count;
}

