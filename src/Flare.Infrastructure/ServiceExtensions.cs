using System.Net.Http;
using System.Threading.Channels;
using Flare.Core.Abstractions;
using Flare.Core.Workers;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Ingestion;
using Flare.Infrastructure.Notifications;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flare.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<FlareDbContext>((sp, o) =>
            o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")));

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

        services.AddOptions<NotificationOptions>()
            .BindConfiguration("Notifications")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();

        // Named HttpClients so tests can override the primary handler per channel without
        // touching the channel registration. 10s ceiling caps dispatcher tick stalls when a
        // webhook hangs — outbox is at-most-once-on-wire, so a slow channel must not block siblings.
        // AllowAutoRedirect=false denies an SSRF redirect chain that could re-emit the webhook
        // bearer-URL to an attacker host. PooledConnectionLifetime caps DNS-rotation latency for
        // webhook hosts that move between Slack/Teams CDNs.
        services.AddHttpClient(SlackNotificationChannel.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });
        services.AddHttpClient(TeamsNotificationChannel.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });

        services.AddSingleton<INotificationChannel, SlackNotificationChannel>();
        services.AddSingleton<INotificationChannel, TeamsNotificationChannel>();

        services.AddHostedService<IngestionWorker>();
        services.AddHostedService<NotificationDispatcher>();
        services.AddHostedService<MetricsAggregator>();
        services.AddHostedService<ActionItemReminderService>();
        services.AddHostedService<OutboxJanitorService>();

        return services;
    }
}
