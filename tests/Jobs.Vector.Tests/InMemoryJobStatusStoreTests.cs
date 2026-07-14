using Jobs.Vector;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jobs.Vector.Tests;

public class InMemoryJobStatusStoreTests
{
    private static InMemoryJobStatusStore CreateStore(TimeProvider time, TimeSpan retention) =>
        new(time, Options.Create(new JobsOptions { StatusRetention = retention }));

    [Fact]
    public void GetStatus_UnknownJobId_ReturnsNull()
    {
        var store = new InMemoryJobStatusStore();
        Assert.Null(store.GetStatus("nope"));
    }

    [Fact]
    public void SetMetadata_PreservesStatusAndProgress()
    {
        var store = new InMemoryJobStatusStore();
        store.SetStatus("j", JobStatus.Processing, 50);

        store.SetMetadata("j", new Dictionary<string, object> { ["peopleImported"] = 3, ["eventsImported"] = 5 });

        var snapshot = store.GetStatus("j");
        Assert.Equal(JobStatus.Processing, snapshot!.Status);
        Assert.Equal(50, snapshot.Progress);
        Assert.Equal(3, snapshot.GetInt("peopleImported"));
        Assert.Equal(5, snapshot.GetInt("eventsImported"));
    }

    [Fact]
    public void SetStatus_AfterSetMetadata_PreservesMetadata()
    {
        var store = new InMemoryJobStatusStore();
        store.SetStatus("j", JobStatus.Processing, 50);
        store.SetMetadata("j", new Dictionary<string, object> { ["peopleImported"] = 3, ["eventsImported"] = 5 });

        store.SetStatus("j", JobStatus.Completed, 100);

        var snapshot = store.GetStatus("j");
        Assert.Equal(JobStatus.Completed, snapshot!.Status);
        Assert.Equal(100, snapshot.Progress);
        Assert.Equal(3, snapshot.GetInt("peopleImported"));
        Assert.Equal(5, snapshot.GetInt("eventsImported"));
    }

    [Fact]
    public void SetMetadata_CalledTwice_MergesRatherThanOverwritingUnrelatedKeys()
    {
        var store = new InMemoryJobStatusStore();
        store.SetMetadata("j", new Dictionary<string, object> { ["a"] = 1 });

        store.SetMetadata("j", new Dictionary<string, object> { ["b"] = 2 });

        var snapshot = store.GetStatus("j")!;
        Assert.Equal(1, snapshot.GetInt("a"));
        Assert.Equal(2, snapshot.GetInt("b"));
    }

    [Fact]
    public void SetMetadata_CalledTwiceWithSameKey_LastWriteWins()
    {
        var store = new InMemoryJobStatusStore();
        store.SetMetadata("j", new Dictionary<string, object> { ["a"] = 1 });

        store.SetMetadata("j", new Dictionary<string, object> { ["a"] = 2 });

        var snapshot = store.GetStatus("j");
        Assert.Equal(2, snapshot!.GetInt("a"));
    }

    [Fact]
    public void GetStatus_WithinRetention_ReturnsSnapshot()
    {
        var time = new FakeTimeProvider();
        var store = CreateStore(time, TimeSpan.FromHours(1));
        store.SetStatus("j", JobStatus.Completed, 100);

        time.Advance(TimeSpan.FromMinutes(59));

        Assert.NotNull(store.GetStatus("j"));
    }

    [Fact]
    public void GetStatus_PastRetention_ReturnsNullAndEvicts()
    {
        var time = new FakeTimeProvider();
        var store = CreateStore(time, TimeSpan.FromHours(1));
        store.SetStatus("j", JobStatus.Completed, 100);

        time.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(store.GetStatus("j"));
    }

    [Fact]
    public void SetStatus_ThenGetStatus_ReturnsMatchingSnapshot()
    {
        var store = new InMemoryJobStatusStore();

        store.SetStatus("job-1", JobStatus.Queued);
        var result = store.GetStatus("job-1");

        Assert.NotNull(result);
        Assert.Equal(JobStatus.Queued, result!.Status);
        Assert.Equal(0, result.Progress);
        Assert.Null(result.Error);
        Assert.Empty(result.Metadata);
    }

    [Fact]
    public void SetStatus_CalledAgain_OverwritesPreviousSnapshot()
    {
        var store = new InMemoryJobStatusStore();

        store.SetStatus("job-1", JobStatus.Queued);
        store.SetStatus("job-1", JobStatus.Processing, progress: 50);
        var result = store.GetStatus("job-1");

        Assert.Equal(JobStatus.Processing, result!.Status);
        Assert.Equal(50, result.Progress);
    }

    [Fact]
    public void SetStatus_Failed_StoresErrorMessage()
    {
        var store = new InMemoryJobStatusStore();

        store.SetStatus("job-1", JobStatus.Failed, error: "boom");
        var result = store.GetStatus("job-1");

        Assert.Equal(JobStatus.Failed, result!.Status);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void SetMetadata_RecordsStringValues()
    {
        var store = new InMemoryJobStatusStore();
        store.SetStatus("job1", JobStatus.Processing);

        store.SetMetadata("job1", new Dictionary<string, object>
        {
            ["personsExported"] = 42,
            ["exportStorageKey"] = "exports/job1.ged",
        });

        var snapshot = store.GetStatus("job1")!;
        Assert.Equal(42, snapshot.GetInt("personsExported"));
        Assert.Equal("exports/job1.ged", snapshot.GetString("exportStorageKey"));
    }

    [Fact]
    public void PruneExpired_RemovesEntriesPastRetention_WithoutGetStatusCall()
    {
        var timeProvider = new FakeTimeProvider();
        var store = CreateStore(timeProvider, TimeSpan.FromMinutes(30));

        store.SetStatus("job-expired", JobStatus.Completed, 100);
        timeProvider.Advance(TimeSpan.FromMinutes(31));
        store.SetStatus("job-fresh", JobStatus.Completed, 100);

        store.PruneExpired();

        // EntryCount (not GetStatus) proves PruneExpired itself removed the expired entry --
        // GetStatus has its own lazy-eviction path that would otherwise make this ambiguous.
        Assert.Equal(1, store.EntryCount);
    }

    [Fact]
    public void PruneExpired_NoExpiredEntries_RemovesNothing()
    {
        var timeProvider = new FakeTimeProvider();
        var store = CreateStore(timeProvider, TimeSpan.FromMinutes(30));

        store.SetStatus("job-1", JobStatus.Completed, 100);
        store.SetStatus("job-2", JobStatus.Processing, 50);

        store.PruneExpired();

        Assert.Equal(2, store.EntryCount);
    }

    [Fact]
    public void GetValue_TypedMetadata_ReturnsTypedValue()
    {
        var store = new InMemoryJobStatusStore();
        store.SetStatus("job1", JobStatus.Processing);

        store.SetMetadata("job1", new Dictionary<string, object>
        {
            ["boolVal"] = true,
            ["doubleVal"] = 123.45,
            ["stringVal"] = "hello"
        });

        var snapshot = store.GetStatus("job1")!;
        Assert.True(snapshot.GetValue<bool>("boolVal"));
        Assert.Equal(123.45, snapshot.GetValue<double>("doubleVal"));
        Assert.Equal("hello", snapshot.GetValue<string>("stringVal"));
    }

    [Fact]
    public void GetValue_UnknownKeyOrWrongType_ReturnsDefault()
    {
        var store = new InMemoryJobStatusStore();
        store.SetStatus("job1", JobStatus.Processing);

        store.SetMetadata("job1", new Dictionary<string, object>
        {
            ["boolVal"] = true
        });

        var snapshot = store.GetStatus("job1")!;
        Assert.False(snapshot.GetValue<bool>("nonexistentKey"));
        Assert.Null(snapshot.GetValue<string>("boolVal")); // wrong type
    }
}


