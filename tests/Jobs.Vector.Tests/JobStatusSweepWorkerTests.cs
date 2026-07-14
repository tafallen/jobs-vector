using Jobs.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jobs.Vector.Tests;

public class JobStatusSweepWorkerTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"Condition not met within {timeout}.");
    }

    [Fact]
    public async Task ExecuteAsync_SweepsExpiredEntriesOnEachTick()
    {
        var timeProvider = new FakeTimeProvider();
        var statusStore = new InMemoryJobStatusStore(timeProvider, Options.Create(new JobsOptions { StatusRetention = TimeSpan.FromMinutes(30) }));
        statusStore.SetStatus("job-expired", JobStatus.Completed, 100);
        timeProvider.Advance(TimeSpan.FromMinutes(31));
        statusStore.SetStatus("job-fresh", JobStatus.Completed, 100);

        var options = Options.Create(new JobsOptions { SweepInterval = TimeSpan.FromMilliseconds(50) });
        var worker = new JobStatusSweepWorker(statusStore, options, NullLogger<JobStatusSweepWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => statusStore.EntryCount == 1, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, statusStore.EntryCount);
    }
}

