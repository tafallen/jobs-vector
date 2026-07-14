using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

public static class JobsServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryJobQueue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName))
            .Validate(o => o.Workers >= 1, "Jobs:Workers must be at least 1.")
            .Validate(o => o.StatusRetention > TimeSpan.Zero, "Jobs:StatusRetention must be greater than zero.")
            .Validate(o => o.QueueCapacity >= 1, "Jobs:QueueCapacity must be at least 1.")
            .Validate(o => o.SweepInterval > TimeSpan.Zero, "Jobs:SweepInterval must be greater than zero.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJobStatusStore, InMemoryJobStatusStore>();
        services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
        services.AddHostedService<BackgroundJobWorker>();
        services.AddHostedService<JobStatusSweepWorker>();

        return services;
    }
}
