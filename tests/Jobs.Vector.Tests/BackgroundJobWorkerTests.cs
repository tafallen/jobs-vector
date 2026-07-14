using Jobs.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Jobs.Vector.Tests;

public class BackgroundJobWorkerTests
{
    private static async Task WaitForStatusAsync(IJobStatusStore store, string jobId, JobStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.GetStatus(jobId)?.Status == expected)
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"Status for '{jobId}' did not reach {expected} within {timeout}.");
    }

    // ExecuteAsync's startup log call has no status-store write to piggyback a wait on (unlike
    // the job-completion tests, which poll WaitForStatusAsync first and so naturally observe the
    // adjacent log call too). Polling the mock's own invocation count gives StartAsync's log call
    // the same kind of synchronization point before StopAsync/Verify run, avoiding an intermittent
    // false failure under concurrent load (observed when other WebApplicationFactory-based tests
    // run background workers of their own in the same process).
    private static async Task WaitForInvocationAsync(Mock<ILogger<BackgroundJobWorker>> mockLogger, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (mockLogger.Invocations.Count > 0)
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"No logger invocation observed within {timeout}.");
    }

    [Fact]
    public async Task ExecuteAsync_JobSucceeds_SetsStatusToCompleted()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var jobRan = new TaskCompletionSource();
        await queue.EnqueueAsync(async ct =>
        {
            await Task.Yield();
            jobRan.SetResult();
        }, "job-1");

        await worker.StartAsync(CancellationToken.None);
        await jobRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-1", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var snapshot = statusStore.GetStatus("job-1");
        Assert.Equal(JobStatus.Completed, snapshot!.Status);
        Assert.Equal(100, snapshot.Progress);
    }

    [Fact]
    public async Task ExecuteAsync_JobThrows_SetsFailedWithMessageAndKeepsProcessingNextJob()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var mockLogger = new Mock<ILogger<BackgroundJobWorker>>();
        var worker = new BackgroundJobWorker(queue, statusStore, options, mockLogger.Object);

        await queue.EnqueueAsync(_ => throw new InvalidOperationException("boom"), "job-fail");

        var secondJobRan = new TaskCompletionSource();
        await queue.EnqueueAsync(async ct =>
        {
            await Task.Yield();
            secondJobRan.SetResult();
        }, "job-after-failure");

        await worker.StartAsync(CancellationToken.None);
        await secondJobRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-fail", JobStatus.Failed, TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-after-failure", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var failedSnapshot = statusStore.GetStatus("job-fail");
        Assert.Equal(JobStatus.Failed, failedSnapshot!.Status);
        Assert.Contains("InvalidOperationException: boom", failedSnapshot.Error);

        mockLogger.Verify(

            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TwoWorkersConfigured_ProcessesTwoJobsConcurrently()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 2 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var job1Started = new TaskCompletionSource();
        var job2Started = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource();

        await queue.EnqueueAsync(async ct =>
        {
            job1Started.SetResult();
            await releaseGate.Task;
        }, "job-concurrent-1");
        await queue.EnqueueAsync(async ct =>
        {
            job2Started.SetResult();
            await releaseGate.Task;
        }, "job-concurrent-2");

        await worker.StartAsync(CancellationToken.None);

        // Both jobs must start before either is released — proves the two
        // configured workers run concurrently rather than one waiting for
        // the other to finish first.
        await Task.WhenAll(
            job1Started.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            job2Started.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        releaseGate.SetResult();

        await WaitForStatusAsync(statusStore, "job-concurrent-1", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-concurrent-2", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_JobInFlight_DoesNotLeaveStatusStuckAtProcessing()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var jobStarted = new TaskCompletionSource();
        await queue.EnqueueAsync(async ct =>
        {
            jobStarted.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, "job-in-flight");

        await worker.StartAsync(CancellationToken.None);
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-in-flight", JobStatus.Processing, TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        var snapshot = statusStore.GetStatus("job-in-flight");
        Assert.Equal(JobStatus.Failed, snapshot!.Status);
        Assert.Equal("Cancelled by shutdown.", snapshot.Error);
    }

    [Fact]
    public async Task ExecuteAsync_OnStart_LogsWorkerCountAtInformationLevel()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 2 });
        var mockLogger = new Mock<ILogger<BackgroundJobWorker>>();
        var worker = new BackgroundJobWorker(queue, statusStore, options, mockLogger.Object);

        await worker.StartAsync(CancellationToken.None);
        await WaitForInvocationAsync(mockLogger, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                null,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobSucceeds_LogsCompletionAtInformationLevel()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var mockLogger = new Mock<ILogger<BackgroundJobWorker>>();
        var worker = new BackgroundJobWorker(queue, statusStore, options, mockLogger.Object);

        await queue.EnqueueAsync(async ct =>
        {
            await Task.Yield();
        }, "job-1");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-1", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("completed successfully")),
                null,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobSucceeds_RecordsDurationInMetadata()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var fakeTimeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance, fakeTimeProvider);

        var jobRan = new TaskCompletionSource();
        await queue.EnqueueAsync(async ct =>
        {
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Yield();
            jobRan.SetResult();
        }, "job-duration-test");

        await worker.StartAsync(CancellationToken.None);
        await jobRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-duration-test", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var snapshot = statusStore.GetStatus("job-duration-test");
        Assert.NotNull(snapshot);
        
        var durationMs = snapshot!.GetValue<double>("durationMs");
        Assert.Equal(250, durationMs);
    }
}


