using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jobs.Vector;

/// <summary>
/// Provides extension methods for registering background job services with <see cref="IServiceCollection"/>.
/// </summary>
public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the background job queue, status store, worker hosting service, and sweep worker.
    /// Binds configuration from the default "Jobs" section name.
    /// </summary>
    /// <param name="services">The service collection to add background job services to.</param>
    /// <param name="configuration">The application configuration containing the "Jobs" section.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
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
        services.AddSingleton<BackgroundJobQueue>();
        services.AddSingleton<IBackgroundJobQueue>(sp =>
        {
            // Resolve the concrete queue first, then wire in the scheduler
            // after both are constructed to break the circular dependency.
            var queue = sp.GetRequiredService<BackgroundJobQueue>();
            var scheduler = sp.GetRequiredService<DelayedJobScheduler>();
            queue.SetScheduler(scheduler);
            return queue;
        });
        services.AddSingleton<DelayedJobScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<DelayedJobScheduler>());
        services.AddHostedService<BackgroundJobWorker>();
        services.AddHostedService<JobStatusSweepWorker>();

        return services;
    }

    /// <summary>
    /// Registers the background job queue, status store, worker hosting service, and sweep worker.
    /// Binds configuration from the default "Jobs" section name.
    /// This is an alias for <see cref="AddInMemoryJobQueue(IServiceCollection, IConfiguration)"/> to match naming conventions.
    /// </summary>
    /// <param name="services">The service collection to add background job services to.</param>
    /// <param name="configuration">The application configuration containing the "Jobs" section.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddInMemoryJobQueue(configuration);
    }
}

