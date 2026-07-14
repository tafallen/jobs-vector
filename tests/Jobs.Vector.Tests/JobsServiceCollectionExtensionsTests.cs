using Jobs.Vector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jobs.Vector.Tests;

public class JobsServiceCollectionExtensionsTests
{
    private static IServiceProvider BuildProvider(IDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryJobQueue(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Workers_NotConfigured_DefaultsToOne()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        var options = provider.GetRequiredService<IOptions<JobsOptions>>().Value;

        Assert.Equal(1, options.Workers);
    }

    [Fact]
    public void Workers_SetToZero_ThrowsOptionsValidationException()
    {
        var provider = BuildProvider(new Dictionary<string, string?> { ["Jobs:Workers"] = "0" });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JobsOptions>>().Value);

        Assert.Contains("Jobs:Workers must be at least 1.", ex.Message);
    }

    [Fact]
    public void AddInMemoryJobQueue_RegistersQueueAndStatusStoreAsSingletons()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        var queue1 = provider.GetRequiredService<IBackgroundJobQueue>();
        var queue2 = provider.GetRequiredService<IBackgroundJobQueue>();
        var store1 = provider.GetRequiredService<IJobStatusStore>();
        var store2 = provider.GetRequiredService<IJobStatusStore>();

        Assert.Same(queue1, queue2);
        Assert.Same(store1, store2);
    }

    [Fact]
    public void AddInMemoryJobQueue_RegistersJobStatusSweepWorkerAsHostedService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryJobQueue(configuration);
        var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, s => s is JobStatusSweepWorker);
    }

    [Fact]
    public void AddBackgroundJobs_RegistersSameServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBackgroundJobs(configuration);
        var provider = services.BuildServiceProvider();

        var queue = provider.GetService<IBackgroundJobQueue>();
        var store = provider.GetService<IJobStatusStore>();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.NotNull(queue);
        Assert.NotNull(store);
        Assert.Contains(hostedServices, s => s is BackgroundJobWorker);
        Assert.Contains(hostedServices, s => s is JobStatusSweepWorker);
    }
}


