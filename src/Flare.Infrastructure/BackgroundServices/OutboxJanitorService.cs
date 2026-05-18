using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.Infrastructure.BackgroundServices;

// Outbox.Payload preserves the full notification body forever after MarkProcessed —
// including user-set titles, OwnerIds, and action-item content. Without a retention
// sweep these rows accumulate indefinitely in DB dumps / replicas / backups. Daily
// trim keeps storage bounded and keeps the historical PII window finite for any
// future compliance / GDPR audit.
public sealed class OutboxJanitorService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxJanitorService> logger) : BackgroundService
{
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay so the first sweep does not race the dispatcher's first batch
        // when the app starts cold.
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "OutboxJanitor sweep failed");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var cutoff = DateTime.UtcNow - Retention;
        var deleted = await db.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("OutboxJanitor: deleted {Count} processed messages older than {Days} days",
                deleted, Retention.TotalDays);
    }
}
