using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.Infrastructure.BackgroundServices;

public sealed class MetricsAggregator(
    IServiceScopeFactory scopeFactory,
    ILogger<MetricsAggregator> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "MetricsAggregator refresh failed");
            }

            await Task.Delay(Interval, ct);
        }
    }

    // Refresh both matviews. Exposed as internal so integration tests can drive a
    // deterministic refresh instead of racing the 5-minute poll loop — same pattern
    // as NotificationDispatcher.ProcessOnceAsync. Serialized back-to-back calls
    // re-snapshot to the same state; REFRESH CONCURRENTLY takes EXCLUSIVE on the
    // matview so overlapping refreshes block — not safe to fan out in parallel.
    internal async Task RefreshAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        // CONCURRENTLY needs the unique index and a populated matview (WITH DATA at migrate time covers both).
        // The two REFRESHes are intentionally NOT wrapped in a transaction — REFRESH MATERIALIZED VIEW
        // CONCURRENTLY can't run inside a transaction block. If the second call fails, MTTA is up to one
        // interval stale until the next tick. Acceptable for a 5-minute MVP metric.
        await db.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mttr_by_service_30d;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mtta_by_service_30d;", ct);
    }
}
