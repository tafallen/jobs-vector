using Jobs.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Jobs.Vector.Tests;

public class BackgroundJobQueueTests
{
    private static BackgroundJobQueue CreateQueue(IJobStatusStore statusStore, int capacity = 100) =>
        new(statusStore, Options.Create(new JobsOptions { QueueCapacity = capacity }), NullLogger<BackgroundJobQueue>.Instance);

    [Fact]
    public async Task EnqueueAsync_SetsStatusToQueued()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-1");

        var status = statusStore.GetStatus("job-1");
        Assert.NotNull(status);
        Assert.Equal(JobStatus.Queued, status!.Status);
    }

    [Fact]
    public async Task EnqueueAsync_ThenDequeueAsync_ReturnsSameJobIdAndDelegate()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
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
        var queue = CreateQueue(statusStore);

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
        var queue = CreateQueue(statusStore);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queue.DequeueAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task EnqueueAsync_WhenQueueIsFull_WaitsUntilSpaceIsAvailable()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore, capacity: 1);

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-1");

        var secondEnqueueTask = queue.EnqueueAsync(_ => Task.CompletedTask, "job-2").AsTask();
        await Task.Delay(50);
        Assert.False(secondEnqueueTask.IsCompleted);

        await queue.DequeueAsync(CancellationToken.None);
        await secondEnqueueTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(secondEnqueueTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task EnqueueAsync_InstantWorker_DoesNotOverwriteWorkerStatusToQueued()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);

        var workerTask = Task.Run(async () =>
        {
            var item = await queue.DequeueAsync(CancellationToken.None);
            statusStore.SetStatus(item.JobId, JobStatus.Processing);
        });

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-race");
        await workerTask;

        var status = statusStore.GetStatus("job-race");
        Assert.NotNull(status);
        Assert.Equal(JobStatus.Processing, status!.Status);
    }

    [Fact]
    public async Task EnqueueAsync_WhenQueueIsFull_LogsWarningAboutBackpressure()
    {
        var statusStore = new InMemoryJobStatusStore();
        var mockLogger = new Mock<ILogger<BackgroundJobQueue>>();
        var queue = new BackgroundJobQueue(
            statusStore,
            Options.Create(new JobsOptions { QueueCapacity = 1 }),
            mockLogger.Object);

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-1");

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await queue.EnqueueAsync(_ => Task.CompletedTask, "job-2", cts.Token);
        });

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Applying backpressure")),
                null,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }
}




