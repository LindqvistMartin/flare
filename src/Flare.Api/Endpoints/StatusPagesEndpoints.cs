using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Caching;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flare.Api.Endpoints;

public static class StatusPagesEndpoints
{
    public static IEndpointRouteBuilder MapStatusPageEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: admin CRUD is currently unauthenticated — gate behind admin auth before any
        // public deploy. The public read endpoint (PublicStatusEndpoints) stays open; only
        // POST/PUT/DELETE on this group need to require admin claims.
        var group = app.MapGroup("/api/v1/status-pages");

        group.MapGet("", async (FlareDbContext db, CancellationToken ct) =>
        {
            var pages = await db.StatusPages
                .AsNoTracking()
                .OrderBy(p => p.Title)
                .ToListAsync(ct);
            return Results.Ok(pages.Select(p => p.ToResponse()).ToList());
        });

        group.MapGet("{id:guid}", async (Guid id, FlareDbContext db, CancellationToken ct) =>
        {
            var page = await db.StatusPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            return page is null
                ? Results.Problem(statusCode: 404, title: "Status page not found")
                : Results.Ok(page.ToResponse());
        });

        group.MapPost("", async (CreateStatusPageRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            if (req.ServiceIds is null || req.ServiceIds.Count == 0)
                return Results.Problem(statusCode: 422, title: "At least one service is required");

            var org = await db.Organizations.FindAsync([req.OrgId], ct);
            if (org is null)
                return Results.Problem(statusCode: 404, title: "Organization not found");

            // FK on jsonb ServiceIds is the endpoint's job — there is no referential integrity
            // at the DB level for ids embedded in jsonb. Batched COUNT avoids N+1.
            var distinctIds = req.ServiceIds.Distinct().ToList();
            var existingCount = await db.Services
                .Where(s => distinctIds.Contains(s.Id))
                .CountAsync(ct);
            if (existingCount != distinctIds.Count)
                return Results.Problem(statusCode: 422, title: "One or more ServiceIds do not exist");

            StatusPage page;
            try
            {
                page = new StatusPage(req.OrgId, req.Slug, req.Title, req.Description, distinctIds);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 422, title: "Invalid status page", detail: ex.Message);
            }

            db.StatusPages.Add(page);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Results.Problem(statusCode: 409, title: "Slug already in use");
            }

            return Results.Created($"/api/v1/status-pages/{page.Id}", page.ToResponse());
        });

        group.MapPut("{id:guid}", async (
            Guid id,
            UpdateStatusPageRequest req,
            FlareDbContext db,
            IStatusPageCache cache,
            CancellationToken ct) =>
        {
            if (req.ServiceIds is null || req.ServiceIds.Count == 0)
                return Results.Problem(statusCode: 422, title: "At least one service is required");

            var page = await db.StatusPages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null)
                return Results.Problem(statusCode: 404, title: "Status page not found");

            var distinctIds = req.ServiceIds.Distinct().ToList();
            var existingCount = await db.Services
                .Where(s => distinctIds.Contains(s.Id))
                .CountAsync(ct);
            if (existingCount != distinctIds.Count)
                return Results.Problem(statusCode: 422, title: "One or more ServiceIds do not exist");

            try
            {
                page.UpdateTitle(req.Title);
                page.UpdateDescription(req.Description);
                page.UpdateServices(distinctIds);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 422, title: "Invalid status page update", detail: ex.Message);
            }

            await db.SaveChangesAsync(ct);
            cache.Invalidate(page.Slug);
            return Results.Ok(page.ToResponse());
        });

        group.MapDelete("{id:guid}", async (
            Guid id,
            FlareDbContext db,
            IStatusPageCache cache,
            CancellationToken ct) =>
        {
            var page = await db.StatusPages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null)
                return Results.Problem(statusCode: 404, title: "Status page not found");

            var slug = page.Slug;
            db.StatusPages.Remove(page);
            await db.SaveChangesAsync(ct);
            // Page row is gone — cache state will TTL naturally on the next read. The
            // version counter and per-slug monitor persist by design; they are bounded by
            // the status_pages table and re-bind cleanly if the slug is re-created.
            cache.Invalidate(slug);
            return Results.NoContent();
        });

        return app;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
