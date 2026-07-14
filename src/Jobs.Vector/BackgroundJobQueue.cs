using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<JobItem> _channel;
    private readonly IJobStatusStore _statusStore;

    public BackgroundJobQueue(IJobStatusStore statusStore, IOptions<JobsOptions> options)
    {
        _statusStore = statusStore;
        _channel = Channel.CreateBounded<JobItem>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public async ValueTask EnqueueAsync(Func<CancellationToken, Task> job, string jobId, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(new JobItem(jobId, job), ct);
        _statusStore.SetStatus(jobId, JobStatus.Queued);
    }

    public ValueTask<JobItem> DequeueAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAsync(ct);
    }
}
