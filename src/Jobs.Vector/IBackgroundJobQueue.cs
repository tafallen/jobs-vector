namespace Jobs.Vector;

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(Func<CancellationToken, Task> job, string jobId, CancellationToken ct = default);

    ValueTask<JobItem> DequeueAsync(CancellationToken ct);
}
