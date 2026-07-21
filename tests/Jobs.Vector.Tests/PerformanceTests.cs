using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Jobs.Vector.Tests;

public class PerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunPerformanceTest_1_Throughput()
    {
        var store = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(store, Options.Create(new JobsOptions { QueueCapacity = 10000 }), NullLogger<BackgroundJobQueue>.Instance);

        const int iterations = 100000;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            await queue.EnqueueAsync(_ => Task.CompletedTask, $"job-{i}");
            await queue.DequeueAsync(CancellationToken.None);
        }

        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        var opsPerSecond = (double)iterations / stopwatch.Elapsed.TotalSeconds;

        _output.WriteLine($"--- Performance Test 1: Throughput ---");
        _output.WriteLine($"Processed {iterations} enqueues/dequeues in {elapsedMs} ms.");
        _output.WriteLine($"Throughput: {opsPerSecond:F2} ops/sec.");
    }

    [Fact]
    public async Task RunPerformanceTest_1b_ZeroClosureThroughput()
    {
        var store = new InMemoryJobStatusStore();
        var queue = new BackgroundJobQueue(store, Options.Create(new JobsOptions { QueueCapacity = 10000 }), NullLogger<BackgroundJobQueue>.Instance);

        const int iterations = 100000;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            await queue.EnqueueAsync(static (state, ct) => Task.CompletedTask, i, $"job-{i}");
            await queue.DequeueAsync(CancellationToken.None);
        }

        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        var opsPerSecond = (double)iterations / stopwatch.Elapsed.TotalSeconds;

        _output.WriteLine($"--- Performance Test 1b: Zero-Closure State-Passing Throughput ---");
        _output.WriteLine($"Processed {iterations} state-passing enqueues/dequeues in {elapsedMs} ms.");
        _output.WriteLine($"Throughput: {opsPerSecond:F2} ops/sec.");
    }

    [Fact]
    public async Task RunPerformanceTest_2_HighConcurrencyContention()
    {
        var store = new InMemoryJobStatusStore();
        var options = Options.Create(new JobsOptions
        {
            QueueCapacity = 100,
            Workers = 4,
            StatusRetention = TimeSpan.FromMinutes(5)
        });
        var queue = new BackgroundJobQueue(store, options, NullLogger<BackgroundJobQueue>.Instance);
        var worker = new BackgroundJobWorker(queue, store, options, NullLogger<BackgroundJobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        const int numEnqueuers = 5;
        const int jobsPerEnqueuer = 1000;
        var startSignal = new TaskCompletionSource();
        
        var enqueuerTasks = Enumerable.Range(0, numEnqueuers).Select(enqueuerId => Task.Run(async () =>
        {
            await startSignal.Task;
            for (int i = 0; i < jobsPerEnqueuer; i++)
            {
                var jobId = $"stress-{enqueuerId}-{i}";
                await queue.EnqueueAsync(async ct =>
                {
                    await Task.Delay(1, ct);
                }, jobId);
            }
        })).ToArray();

        var stopwatch = Stopwatch.StartNew();
        startSignal.SetResult();
        await Task.WhenAll(enqueuerTasks);

        var totalJobs = numEnqueuers * jobsPerEnqueuer;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        
        while (DateTime.UtcNow < deadline)
        {
            int completedCount = 0;
            for (int e = 0; e < numEnqueuers; e++)
            {
                for (int j = 0; j < jobsPerEnqueuer; j++)
                {
                    var status = store.GetStatus($"stress-{e}-{j}");
                    if (status?.Status == JobStatus.Completed || status?.Status == JobStatus.Failed)
                    {
                        completedCount++;
                    }
                }
            }

            if (completedCount >= totalJobs)
            {
                break;
            }
            await Task.Delay(100);
        }

        stopwatch.Stop();
        await worker.StopAsync(CancellationToken.None);

        var durations = new System.Collections.Generic.List<double>();
        for (int e = 0; e < numEnqueuers; e++)
        {
            for (int j = 0; j < jobsPerEnqueuer; j++)
            {
                var status = store.GetStatus($"stress-{e}-{j}");
                if (status != null)
                {
                    durations.Add(status.GetValue<double>("durationMs"));
                }
            }
        }

        durations.Sort();
        var p50 = durations[durations.Count / 2];
        var p95 = durations[(int)(durations.Count * 0.95)];
        var p99 = durations[(int)(durations.Count * 0.99)];

        _output.WriteLine($"--- Performance Test 2: Concurrency & Stress ---");
        _output.WriteLine($"Successfully processed {durations.Count} / {totalJobs} jobs with 4 concurrent workers.");
        _output.WriteLine($"Total Stress Test Time: {stopwatch.ElapsedMilliseconds} ms.");
        _output.WriteLine($"Job Execution Latency (P50): {p50:F2} ms");
        _output.WriteLine($"Job Execution Latency (P95): {p95:F2} ms");
        _output.WriteLine($"Job Execution Latency (P99): {p99:F2} ms");
    }
}
