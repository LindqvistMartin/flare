using System.Threading.Channels;
using Flare.Core.Abstractions;
using Flare.Core.Workers;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Ingestion;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flare.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FlareDbContext>(o => o.UseNpgsql(connectionString));

        var channel = Channel.CreateBounded<IngestionJob>(
            new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite });

        // Register writer and reader separately so endpoints get write-only access
        // and the worker gets read-only access — principle of least privilege.
        services.AddSingleton(channel.Writer);
        services.AddSingleton(channel.Reader);

        services.AddSingleton<IAlertIngestionAdapter, PrometheusAlertIngestionAdapter>();
        services.AddSingleton<IAlertIngestionAdapter, GrafanaAlertIngestionAdapter>();
        services.AddSingleton<IAlertIngestionAdapter, PulseWatchAlertIngestionAdapter>();
        services.AddSingleton<IAlertIngestionAdapter, GenericWebhookAdapter>();

        services.AddHostedService<IngestionWorker>();
        services.AddHostedService<NotificationDispatcher>();
        services.AddHostedService<MetricsAggregator>();

        return services;
    }
}
