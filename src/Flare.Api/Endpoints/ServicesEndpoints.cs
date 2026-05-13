using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flare.Api.Endpoints;

public static class ServicesEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/services");

        group.MapGet("", async (FlareDbContext db, CancellationToken ct) =>
        {
            var services = await db.Services
                .OrderBy(s => s.Name)
                .Select(s => s.ToResponse())
                .ToListAsync(ct);
            return Results.Ok(services);
        });

        group.MapPost("", async (CreateServiceRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            var team = await db.Teams.FindAsync([req.TeamId], ct);
            if (team is null)
                return Results.Problem(statusCode: 404, title: "Team not found");

            var service = new Service(req.TeamId, req.Name, req.Description);
            db.Services.Add(service);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/services/{service.Id}", service.ToResponse());
        });

        group.MapGet("{id:guid}", async (Guid id, FlareDbContext db, CancellationToken ct) =>
        {
            var service = await db.Services.FindAsync([id], ct);
            return service is null
                ? Results.Problem(statusCode: 404, title: "Service not found")
                : Results.Ok(service.ToResponse());
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateRunbookRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            var service = await db.Services.FindAsync([id], ct);
            if (service is null)
                return Results.Problem(statusCode: 404, title: "Service not found");

            service.UpdateRunbook(req.RunbookBody);
            await db.SaveChangesAsync(ct);
            return Results.Ok(service.ToResponse());
        });

        return app;
    }
}
