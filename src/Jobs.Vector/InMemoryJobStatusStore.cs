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

    private sealed class Entry
    {
        private readonly object _lock = new();

        public JobStatus Status { get; private set; }
        public int Progress { get; private set; }
        public string? Error { get; private set; }
        public IReadOnlyDictionary<string, object> Metadata { get; private set; }
        public DateTimeOffset UpdatedAt { get; private set; }

        public Entry(JobStatus status, int progress, string? error, IReadOnlyDictionary<string, object> metadata, DateTimeOffset updatedAt)
        {
            Status = status;
            Progress = progress;
            Error = error;
            Metadata = metadata;
            UpdatedAt = updatedAt;
        }

        public JobStatusSnapshot ToSnapshot()
        {
            lock (_lock)
            {
                return new JobStatusSnapshot(Status, Progress, Error, Metadata);
            }
        }

        public void UpdateStatus(JobStatus status, int progress, string? error, DateTimeOffset updatedAt)
        {
            lock (_lock)
            {
                Status = status;
                Progress = progress;
                Error = error;
                UpdatedAt = updatedAt;
            }
        }

        public void UpdateMetadata(IReadOnlyDictionary<string, object> metadata, DateTimeOffset updatedAt)
        {
            lock (_lock)
            {
                Metadata = metadata;
                UpdatedAt = updatedAt;
            }
        }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(Environment.ProcessorCount * 2, 1024, StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string JobId, DateTimeOffset Expiry)> _evictionQueue = new();
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
        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(jobId, out var existing))
        {
            existing.UpdateStatus(status, progress, error, now);
        }
        else
        {
            var newEntry = new Entry(status, progress, error, EmptyMetadata, now);
            if (_entries.TryAdd(jobId, newEntry))
            {
                _evictionQueue.Enqueue((jobId, now + _retention));
            }
            else
            {
                if (_entries.TryGetValue(jobId, out existing))
                {
                    existing.UpdateStatus(status, progress, error, now);
                }
            }
        }
    }

    /// <inheritdoc />
    public void SetMetadata(string jobId, IReadOnlyDictionary<string, object> metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(jobId, out var existing))
        {
            lock (existing)
            {
                if (existing.UpdatedAt != now || existing.Metadata != metadata)
                {
                    var merged = Merge(existing.Metadata, metadata);
                    existing.UpdateMetadata(merged, now);
                }
            }
        }
        else
        {
            var newEntry = new Entry(JobStatus.Processing, 0, null, metadata, now);
            if (_entries.TryAdd(jobId, newEntry))
            {
                _evictionQueue.Enqueue((jobId, now + _retention));
            }
            else
            {
                if (_entries.TryGetValue(jobId, out existing))
                {
                    lock (existing)
                    {
                        var merged = Merge(existing.Metadata, metadata);
                        existing.UpdateMetadata(merged, now);
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public void SetMetadata(string jobId, string key, object value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(jobId, out var existing))
        {
            lock (existing)
            {
                var merged = Merge(existing.Metadata, key, value);
                existing.UpdateMetadata(merged, now);
            }
        }
        else
        {
            var initialMetadata = new Dictionary<string, object> { [key] = value };
            var newEntry = new Entry(JobStatus.Processing, 0, null, initialMetadata, now);
            if (_entries.TryAdd(jobId, newEntry))
            {
                _evictionQueue.Enqueue((jobId, now + _retention));
            }
            else
            {
                if (_entries.TryGetValue(jobId, out existing))
                {
                    lock (existing)
                    {
                        var merged = Merge(existing.Metadata, key, value);
                        existing.UpdateMetadata(merged, now);
                    }
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, object> Merge(
        IReadOnlyDictionary<string, object> existing, string key, object value)
    {
        var merged = new Dictionary<string, object>(existing);
        merged[key] = value;
        return merged;
    }

    private static IReadOnlyDictionary<string, object> Merge(
        IReadOnlyDictionary<string, object> existing, IReadOnlyDictionary<string, object> updates)
    {
        if (updates == null || updates.Count == 0)
        {
            return existing;
        }
        if (existing == null || existing.Count == 0)
        {
            return updates;
        }

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
            _entries.TryRemove(KeyValuePair.Create(jobId, entry));
            return null;
        }

        return entry.ToSnapshot();
    }

    /// <inheritdoc />
    public void PruneExpired()
    {
        var now = _timeProvider.GetUtcNow();
        while (_evictionQueue.TryPeek(out var item))
        {
            if (now >= item.Expiry)
            {
                if (_evictionQueue.TryDequeue(out item))
                {
                    if (_entries.TryGetValue(item.JobId, out var entry))
                    {
                        if (now - entry.UpdatedAt > _retention)
                        {
                            _entries.TryRemove(KeyValuePair.Create(item.JobId, entry));
                        }
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Gets the count of active status entries currently in the store. Primarily for diagnostic or testing purposes.
    /// </summary>
    public int EntryCount => _entries.Count;
}


