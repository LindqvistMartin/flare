using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flare.Api.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/metrics");

        group.MapGet("mttr", async (FlareDbContext db, CancellationToken ct) =>
        {
            var rows = await db.MttrByService30d
                .OrderBy(r => r.ServiceName)
                .Select(r => new ServiceMttrResponse(
                    r.ServiceId, r.ServiceName, r.IncidentCount, r.AvgMttrMs, r.P50MttrMs))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapGet("mtta", async (FlareDbContext db, CancellationToken ct) =>
        {
            var rows = await db.MttaByService30d
                .OrderBy(r => r.ServiceName)
                .Select(r => new ServiceMttaResponse(
                    r.ServiceId, r.ServiceName, r.IncidentCount, r.AvgMttaMs, r.P50MttaMs))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapGet("dashboard", async (FlareDbContext db, CancellationToken ct) =>
        {
            var openCount = await db.Incidents
                .CountAsync(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed, ct);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var overdueCount = await db.ActionItems
                .CountAsync(a => a.Status != ActionItemStatus.Done && a.DueDate != null && a.DueDate < today, ct);

            // Service-weighted average over services that had at least one resolved incident in the window.
            // Per-incident weighting can wait for a dedicated trend endpoint.
            var mttrAvg = await db.MttrByService30d
                .Where(r => r.IncidentCount > 0)
                .Select(r => (double?)r.AvgMttrMs)
                .AverageAsync(ct) ?? 0d;

            return Results.Ok(new DashboardResponse(openCount, overdueCount, (long)Math.Round(mttrAvg)));
        });

        return app;
    }
}
