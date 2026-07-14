using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

public class JobStatusSweepWorker : BackgroundService
{
    private readonly IJobStatusStore _statusStore;
    private readonly JobsOptions _options;
    private readonly ILogger<JobStatusSweepWorker> _logger;

    public JobStatusSweepWorker(IJobStatusStore statusStore, IOptions<JobsOptions> options, ILogger<JobStatusSweepWorker> logger)
    {
        _statusStore = statusStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job status sweep worker starting with interval {SweepInterval}", _options.SweepInterval);

        using var timer = new PeriodicTimer(_options.SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _statusStore.PruneExpired();
                _logger.LogDebug("Job status sweep completed");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown via PeriodicTimer.WaitForNextTickAsync observing cancellation.
        }
    }
}
