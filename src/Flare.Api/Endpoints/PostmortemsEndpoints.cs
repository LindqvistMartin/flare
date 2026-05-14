using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Core.Services;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flare.Api.Endpoints;

public static class PostmortemsEndpoints
{
    public static IEndpointRouteBuilder MapPostmortemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/incidents/{id:guid}/postmortem");

        group.MapPost("/generate", async (
            Guid id,
            FlareDbContext db,
            PostmortemDraftBuilder builder,
            CancellationToken ct) =>
        {
            var incident = await db.Incidents
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

            var draft = builder.Build(incident, events);

            if (incident.Postmortem is null)
            {
                var pm = new Postmortem(incident.Id, draft.Impact, draft.Timeline, draft.RootCause);
                db.Postmortems.Add(pm);
            }
            else
            {
                incident.Postmortem.Regenerate(draft.Impact, draft.Timeline, draft.RootCause);
            }

            await db.SaveChangesAsync(ct);

            var fresh = await db.Postmortems.AsNoTracking()
                .FirstAsync(p => p.IncidentId == id, ct);
            return Results.Ok(fresh.ToResponse());
        });

        return app;
    }
}
