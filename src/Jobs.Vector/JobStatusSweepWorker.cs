using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// A hosted service that periodically runs a sweep to evict expired jobs from the status store.
/// </summary>
public class JobStatusSweepWorker : BackgroundService
{
    private readonly IJobStatusStore _statusStore;
    private readonly JobsOptions _options;
    private readonly ILogger<JobStatusSweepWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobStatusSweepWorker"/> class.
    /// </summary>
    /// <param name="statusStore">The store used to persist and sweep job statuses.</param>
    /// <param name="options">The configuration options for background jobs.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public JobStatusSweepWorker(IJobStatusStore statusStore, IOptions<JobsOptions> options, ILogger<JobStatusSweepWorker> logger)
    {
        _statusStore = statusStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
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

