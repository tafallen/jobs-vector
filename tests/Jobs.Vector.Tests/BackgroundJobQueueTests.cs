using Jobs.Vector;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jobs.Vector.Tests;

public class BackgroundJobQueueTests
{
    [Fact]
    public async Task EnqueueAsync_SetsStatusToQueued()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()));

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-1");

        var status = statusStore.GetStatus("job-1");
        Assert.NotNull(status);
        Assert.Equal(JobStatus.Queued, status!.Status);
    }

    [Fact]
    public async Task EnqueueAsync_ThenDequeueAsync_ReturnsSameJobIdAndDelegate()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()));
        Func<CancellationToken, Task> job = _ => Task.CompletedTask;

        await queue.EnqueueAsync(job, "job-1");
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal("job-1", dequeued.JobId);
        Assert.Same(job, dequeued.Job);
    }

    [Fact]
    public async Task DequeueAsync_BeforeAnyEnqueue_WaitsUntilItemIsAvailable()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()));

        var dequeueTask = queue.DequeueAsync(CancellationToken.None).AsTask();
        Assert.False(dequeueTask.IsCompleted);

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-1");
        var dequeued = await dequeueTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("job-1", dequeued.JobId);
    }

    [Fact]
    public async Task DequeueAsync_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queue.DequeueAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task EnqueueAsync_WhenQueueIsFull_WaitsUntilSpaceIsAvailable()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions { QueueCapacity = 1 }));

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-1");

        var secondEnqueueTask = queue.EnqueueAsync(_ => Task.CompletedTask, "job-2").AsTask();
        await Task.Delay(50);
        Assert.False(secondEnqueueTask.IsCompleted);

        await queue.DequeueAsync(CancellationToken.None);
        await secondEnqueueTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(secondEnqueueTask.IsCompletedSuccessfully);
    }
}

