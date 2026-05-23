using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flare.Api.Endpoints;

public static class ActionItemsEndpoints
{
    public static IEndpointRouteBuilder MapActionItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/postmortems/{postmortemId:guid}/action-items",
            async (Guid postmortemId, FlareDbContext db, CancellationToken ct) =>
        {
            var exists = await db.Postmortems.AnyAsync(p => p.Id == postmortemId, ct);
            if (!exists)
                return Results.Problem(statusCode: 404, title: "Postmortem not found");

            var items = await db.ActionItems
                .Where(a => a.PostmortemId == postmortemId)
                .OrderBy(a => a.CreatedAt)
                .Select(a => a.ToResponse())
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        app.MapPost("/api/v1/postmortems/{postmortemId:guid}/action-items",
            async (Guid postmortemId, CreateActionItemRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Results.Problem(statusCode: 400, title: "Title is required");

            var exists = await db.Postmortems.AnyAsync(p => p.Id == postmortemId, ct);
            if (!exists)
                return Results.Problem(statusCode: 404, title: "Postmortem not found");

            var item = new ActionItem(postmortemId, req.Title, req.OwnerId, req.DueDate);
            db.ActionItems.Add(item);
            await db.SaveChangesAsync(ct);
            // Location points to the individual-item GET endpoint
            return Results.Created($"/api/v1/action-items/{item.Id}", item.ToResponse());
        });

        app.MapGet("/api/v1/action-items/{id:guid}",
            async (Guid id, FlareDbContext db, CancellationToken ct) =>
        {
            var item = await db.ActionItems.FindAsync([id], ct);
            return item is null
                ? Results.Problem(statusCode: 404, title: "Action item not found")
                : Results.Ok(item.ToResponse());
        });

        app.MapPatch("/api/v1/action-items/{id:guid}",
            async (Guid id, PatchActionItemRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            var item = await db.ActionItems.FindAsync([id], ct);
            if (item is null)
                return Results.Problem(statusCode: 404, title: "Action item not found");

            if (req.Status is not null)
            {
                if (!Enum.TryParse<ActionItemStatus>(req.Status, ignoreCase: true, out var next))
                    return Results.Problem(statusCode: 400, title: "Invalid status", detail: $"Unknown status: {req.Status}");
                item.Transition(next);
            }

            // Null means "field absent / don't change". Clients that omit a field and clients
            // that explicitly send null are indistinguishable with a plain nullable record —
            // both produce null here. Convention: null = skip, non-null = apply.
            if (req.OwnerId.HasValue)  item.UpdateOwner(req.OwnerId.Value);
            if (req.DueDate.HasValue)  item.UpdateDueDate(req.DueDate.Value);

            await db.SaveChangesAsync(ct);
            return Results.Ok(item.ToResponse());
        });

        app.MapGet("/api/v1/action-items", async (
            FlareDbContext db,
            bool? overdue,
            Guid? postmortemId,
            CancellationToken ct) =>
        {
            var query = db.ActionItems.AsQueryable();

            if (postmortemId.HasValue)
                query = query.Where(a => a.PostmortemId == postmortemId.Value);

            if (overdue == true)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                query = query.Where(a => a.DueDate < today && a.Status != ActionItemStatus.Done);
            }

            var items = await query
                .OrderBy(a => a.DueDate)
                .Select(a => a.ToResponse())
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        return app;
    }
}
