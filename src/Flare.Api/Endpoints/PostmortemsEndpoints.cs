using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Core.Services;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flare.Api.Endpoints;

public static class PostmortemsEndpoints
{
    private const string UniqueViolationSqlState = "23505";
    private const string PostmortemIncidentIdIndex = "IX_Postmortems_IncidentId";

    public static IEndpointRouteBuilder MapPostmortemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/incidents/{id:guid}/postmortem");

        // Empty path so the route template matches the group prefix exactly.
        // Using "/" here appends a literal slash and produces a 404 on the
        // canonical no-trailing-slash URL the rest of the API ships.
        group.MapGet("", async (
            Guid id,
            FlareDbContext db,
            CancellationToken ct) =>
        {
            var pm = await db.Postmortems
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IncidentId == id, ct);

            return pm is null
                ? Results.Problem(statusCode: 404, title: "Postmortem not found")
                : Results.Ok(pm.ToResponse());
        });

        group.MapPost("/generate", async (
            Guid id,
            FlareDbContext db,
            PostmortemDraftBuilder builder,
            CancellationToken ct) =>
        {
            var incident = await db.Incidents
                .Include(i => i.Service)
                .Include(i => i.Postmortem)
                .FirstOrDefaultAsync(i => i.Id == id, ct);

            if (incident is null)
                return Results.Problem(statusCode: 404, title: "Incident not found");

            if (incident.Postmortem is { Status: PostmortemStatus.Published })
                return Results.Problem(statusCode: 409, title: "Postmortem already published");

            var events = await db.IncidentEvents
                .Where(e => e.IncidentId == id)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct);

            var draft = builder.Build(incident, events, incident.Service?.Name);

            try
            {
                if (incident.Postmortem is null)
                {
                    var pm = new Postmortem(incident.Id, draft.Impact, draft.Timeline, draft.RootCause);
                    db.Postmortems.Add(pm);
                    await db.SaveChangesAsync(ct);
                    return Results.Created(
                        $"/api/v1/incidents/{incident.Id}/postmortem",
                        pm.ToResponse());
                }

                incident.Postmortem.Regenerate(draft.Impact, draft.Timeline, draft.RootCause);
                await db.SaveChangesAsync(ct);
                return Results.Ok(incident.Postmortem.ToResponse());
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another request created the postmortem between our read and insert.
                return Results.Problem(statusCode: 409, title: "Postmortem already exists for this incident");
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another regeneration committed against the same Draft after we loaded it.
                return Results.Problem(statusCode: 409, title: "Postmortem was modified concurrently");
            }
        });

        group.MapPost("/publish", async (
            Guid id,
            FlareDbContext db,
            CancellationToken ct) =>
        {
            var pm = await db.Postmortems
                .FirstOrDefaultAsync(p => p.IncidentId == id, ct);

            if (pm is null)
                return Results.Problem(statusCode: 404, title: "Postmortem not found");

            // Idempotent: republishing a published postmortem returns the current
            // representation. The domain's Publish() method does not guard
            // against repeat calls; the endpoint enforces the no-op contract.
            if (pm.Status == PostmortemStatus.Published)
                return Results.Ok(pm.ToResponse());

            pm.Publish();

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(pm.ToResponse());
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent Regenerate landed between our read and write.
                // The xmin shadow token on Postmortem flagged the row as stale;
                // surface as 409 so the client retries with fresh state.
                return Results.Problem(statusCode: 409, title: "Postmortem was modified concurrently");
            }
        });

        return app;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == UniqueViolationSqlState
        && pg.ConstraintName == PostmortemIncidentIdIndex;
}
