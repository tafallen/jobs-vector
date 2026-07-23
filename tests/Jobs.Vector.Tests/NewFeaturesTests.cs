using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jobs.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jobs.Vector.Tests;

public class CancellationTests
{
    private static BackgroundJobQueue CreateQueue(IJobStatusStore statusStore, int capacity = 100) =>
        new(statusStore, Options.Create(new JobsOptions { QueueCapacity = capacity }), NullLogger<BackgroundJobQueue>.Instance);

    private static async Task WaitForStatusAsync(IJobStatusStore store, string jobId, JobStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.GetStatus(jobId)?.Status == expected) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Status for '{jobId}' did not reach {expected} within {timeout}.");
    }

    [Fact]
    public async Task CancelJob_WhileRunning_SetsStatusToFailed()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var jobStarted = new TaskCompletionSource();
        await queue.EnqueueAsync(async ct =>
        {
            jobStarted.SetResult();
            await Task.Delay(Timeout.Infinite, ct); // waits until cancelled
        }, "job-cancel-running");

        await worker.StartAsync(CancellationToken.None);
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = queue.CancelJob("job-cancel-running");
        Assert.True(cancelled);

        await WaitForStatusAsync(statusStore, "job-cancel-running", JobStatus.Failed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var snapshot = statusStore.GetStatus("job-cancel-running");
        Assert.Equal(JobStatus.Failed, snapshot!.Status);
        Assert.Equal("Cancelled by request.", snapshot.Error);
    }

    [Fact]
    public async Task CancelJob_AfterCompletion_ReturnsFalse()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        await queue.EnqueueAsync(ct => Task.CompletedTask, "job-already-done");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-already-done", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // The per-job CTS was removed after completion
        var result = queue.CancelJob("job-already-done");
        Assert.False(result);
    }

    [Fact]
    public async Task CancelJob_UnknownJobId_ReturnsFalse()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);

        var result = queue.CancelJob("nonexistent-job");
        Assert.False(result);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CancelJob_DoesNotAffectOtherConcurrentJobs()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
        var options = Options.Create(new JobsOptions { Workers = 2 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var job1Started = new TaskCompletionSource();
        var job2Ran = new TaskCompletionSource();

        await queue.EnqueueAsync(async ct =>
        {
            job1Started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, "job-cancel-me");

        await queue.EnqueueAsync(async ct =>
        {
            await Task.Yield();
            job2Ran.SetResult();
        }, "job-keep-me");

        await worker.StartAsync(CancellationToken.None);
        await job1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        queue.CancelJob("job-cancel-me");

        await job2Ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-keep-me", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, "job-cancel-me", JobStatus.Failed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Completed, statusStore.GetStatus("job-keep-me")!.Status);
        Assert.Equal(JobStatus.Failed, statusStore.GetStatus("job-cancel-me")!.Status);
    }

    [Fact]
    public async Task CancelJob_WithGuidId_UsesExtensionMethod()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
        var jobId = Guid.NewGuid();

        var jobStarted = new TaskCompletionSource();
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        await queue.EnqueueAsync(async ct =>
        {
            jobStarted.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }, jobId);

        await worker.StartAsync(CancellationToken.None);
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var result = queue.CancelJob(jobId);
        Assert.True(result);

        await WaitForStatusAsync(statusStore, jobId.ToString("N"), JobStatus.Failed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }
}

public class RetryTests
{
    private static BackgroundJobQueue CreateQueue(IJobStatusStore statusStore, JobsOptions? opts = null) =>
        new(statusStore, Options.Create(opts ?? new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);

    private static async Task WaitForStatusAsync(IJobStatusStore store, string jobId, JobStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.GetStatus(jobId)?.Status == expected) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Status for '{jobId}' did not reach {expected} within {timeout}.");
    }

    [Fact]
    public async Task FailedJob_WithMaxRetries_RetriesConfiguredNumberOfTimes()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore, new JobsOptions { DefaultMaxRetries = 2 });
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var attemptCount = 0;
        await queue.EnqueueAsync(ct =>
        {
            Interlocked.Increment(ref attemptCount);
            throw new InvalidOperationException("transient failure");
        }, "job-retry");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-retry", JobStatus.Failed, TimeSpan.FromSeconds(30));
        await worker.StopAsync(CancellationToken.None);

        // Should have been attempted 1 original + 2 retries = 3 times
        Assert.Equal(3, attemptCount);
        Assert.Equal(JobStatus.Failed, statusStore.GetStatus("job-retry")!.Status);
    }

    [Fact]
    public async Task FailedJob_SucceedsOnRetry_SetsStatusToCompleted()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore, new JobsOptions { DefaultMaxRetries = 3 });
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var attemptCount = 0;
        await queue.EnqueueAsync(ct =>
        {
            var attempt = Interlocked.Increment(ref attemptCount);
            if (attempt < 3) throw new InvalidOperationException("not yet");
            return Task.CompletedTask;
        }, "job-retry-success");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-retry-success", JobStatus.Completed, TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(3, attemptCount);
        Assert.Equal(JobStatus.Completed, statusStore.GetStatus("job-retry-success")!.Status);
    }

    [Fact]
    public async Task FailedJob_WithNoRetries_SetsFailedAfterFirstAttempt()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore, new JobsOptions { DefaultMaxRetries = 0 });
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var attemptCount = 0;
        await queue.EnqueueAsync(ct =>
        {
            Interlocked.Increment(ref attemptCount);
            throw new Exception("instant fail");
        }, "job-no-retry");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-no-retry", JobStatus.Failed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, attemptCount);
    }
}

public class LifecycleEventTests
{
    private static BackgroundJobQueue CreateQueue(IJobStatusStore statusStore) =>
        new(statusStore, Options.Create(new JobsOptions()), NullLogger<BackgroundJobQueue>.Instance);

    private static async Task WaitForStatusAsync(IJobStatusStore store, string jobId, JobStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.GetStatus(jobId)?.Status == expected) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Status for '{jobId}' did not reach {expected} within {timeout}.");
    }

    [Fact]
    public async Task EnqueueAsync_FiresOnJobEnqueuedEvent()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);

        string? capturedId = null;
        queue.OnJobEnqueued += id => capturedId = id;

        await queue.EnqueueAsync(_ => Task.CompletedTask, "job-enqueued-event");

        Assert.Equal("job-enqueued-event", capturedId);
    }

    [Fact]
    public async Task WorkerExecution_FiresOnJobStartedAndCompleted()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var startedIds = new List<string>();
        var completedIds = new List<string>();
        queue.OnJobStarted += id => startedIds.Add(id);
        queue.OnJobCompleted += id => completedIds.Add(id);

        await queue.EnqueueAsync(async ct => await Task.Yield(), "job-lifecycle");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-lifecycle", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains("job-lifecycle", startedIds);
        Assert.Contains("job-lifecycle", completedIds);
    }

    [Fact]
    public async Task WorkerExecution_FailedJob_FiresOnJobFailedEvent()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = CreateQueue(statusStore);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        string? failedId = null;
        Exception? capturedException = null;
        queue.OnJobFailed += (id, ex) => { failedId = id; capturedException = ex; };

        await queue.EnqueueAsync(_ => throw new InvalidOperationException("event-fail"), "job-fail-event");

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(statusStore, "job-fail-event", JobStatus.Failed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal("job-fail-event", failedId);
        Assert.IsType<InvalidOperationException>(capturedException);
    }
}

public class OpenTelemetryTests
{
    [Fact]
    public async Task JobExecution_CreatesActivityWithJobIdTag()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(
            statusStore,
            Options.Create(new JobsOptions { Workers = 1 }),
            NullLogger<BackgroundJobQueue>.Instance);
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Jobs.Vector",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        await queue.EnqueueAsync(ct => Task.CompletedTask, "job-otel");

        await worker.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && capturedActivity == null)
            await Task.Delay(10);

        await worker.StopAsync(CancellationToken.None);

        Assert.NotNull(capturedActivity);
        Assert.Equal("job.execute", capturedActivity!.OperationName);
        Assert.Equal("job-otel", capturedActivity.GetTagItem("job.id"));
    }
}

public class DelayedJobTests
{
    private static async Task WaitForStatusAsync(IJobStatusStore store, string jobId, JobStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.GetStatus(jobId)?.Status == expected) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Status for '{jobId}' did not reach {expected} within {timeout}.");
    }

    [Fact]
    public async Task EnqueueDelayedAsync_JobRunsAfterDelay()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(
            statusStore,
            Options.Create(new JobsOptions { Workers = 1 }),
            NullLogger<BackgroundJobQueue>.Instance);

        var scheduler = new DelayedJobScheduler(queue, NullLogger<DelayedJobScheduler>.Instance);
        queue.SetScheduler(scheduler);

        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        var jobRan = new TaskCompletionSource();
        var enqueueTime = DateTime.UtcNow;

        using var schedulerCts = new CancellationTokenSource();
        var schedulerTask = scheduler.StartAsync(schedulerCts.Token);

        await queue.EnqueueDelayedAsync(async ct =>
        {
            await Task.Yield();
            jobRan.SetResult();
        }, TimeSpan.FromMilliseconds(200), "job-delayed");

        // Job should NOT be in the active queue immediately (status stays Queued)
        Assert.Equal(JobStatus.Queued, statusStore.GetStatus("job-delayed")!.Status);
        Assert.False(jobRan.Task.IsCompleted);

        await worker.StartAsync(CancellationToken.None);

        await jobRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var elapsed = DateTime.UtcNow - enqueueTime;

        // At least 180ms should have elapsed (allowing some timer jitter)
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(180), $"Job ran too early: elapsed={elapsed.TotalMilliseconds}ms");

        await WaitForStatusAsync(statusStore, "job-delayed", JobStatus.Completed, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EnqueueDelayedAsync_WithGuidId_JobRunsAfterDelay()
    {
        var statusStore = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(
            statusStore,
            Options.Create(new JobsOptions { Workers = 1 }),
            NullLogger<BackgroundJobQueue>.Instance);

        var scheduler = new DelayedJobScheduler(queue, NullLogger<DelayedJobScheduler>.Instance);
        queue.SetScheduler(scheduler);

        var jobId = Guid.NewGuid();
        var options = Options.Create(new JobsOptions { Workers = 1 });
        var worker = new BackgroundJobWorker(queue, statusStore, options, NullLogger<BackgroundJobWorker>.Instance);

        await scheduler.StartAsync(CancellationToken.None);

        var jobRan = new TaskCompletionSource();
        await queue.EnqueueDelayedAsync(async ct =>
        {
            await Task.Yield();
            jobRan.SetResult();
        }, TimeSpan.FromMilliseconds(100), jobId);

        await worker.StartAsync(CancellationToken.None);
        await jobRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(statusStore, jobId.ToString("N"), JobStatus.Completed, TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);
    }
}
