using System.Text.Json;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flare.Api.Endpoints;

public static class IncidentsEndpoints
{
    private static readonly HashSet<IncidentEventType> AllowedExternalEventTypes =
        [IncidentEventType.CommentAdded, IncidentEventType.SeverityChanged];

    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/incidents");

        group.MapGet("", async (FlareDbContext db,
            string? status, Guid? serviceId, string? severity,
            CancellationToken ct) =>
        {
            var query = db.Incidents.AsQueryable();

            if (status is not null && Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var s))
                query = query.Where(i => i.Status == s);
            if (serviceId is not null)
                query = query.Where(i => i.ServiceId == serviceId);
            if (severity is not null && Enum.TryParse<IncidentSeverity>(severity, ignoreCase: true, out var sev))
                query = query.Where(i => i.Severity == sev);

            var results = await query
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => i.ToResponse())
                .ToListAsync(ct);

            return Results.Ok(results);
        });

        group.MapPost("", async (CreateIncidentRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Results.Problem(statusCode: 400, title: "Title is required");
            if (string.IsNullOrWhiteSpace(req.Severity))
                return Results.Problem(statusCode: 400, title: "Severity is required");
            if (!Enum.TryParse<IncidentSeverity>(req.Severity, ignoreCase: true, out var severity))
                return Results.Problem(statusCode: 400, title: "Invalid severity", detail: $"Unknown severity: {req.Severity}");
            if (req.ServiceId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "ServiceId is required");

            var service = await db.Services.FindAsync([req.ServiceId], ct);
            if (service is null)
                return Results.Problem(statusCode: 404, title: "Service not found");

            var incident = new Incident(req.ServiceId, req.Title, severity);
            var evt = new IncidentEvent(incident.Id, IncidentEventType.Created,
                JsonSerializer.Serialize(new { req.Title, Severity = severity.ToString() }), actorId: null);
            var outbox = new OutboxMessage(
                "IncidentCreated",
                JsonSerializer.Serialize(new { IncidentId = incident.Id, Source = "manual" }));

            db.Incidents.Add(incident);
            db.IncidentEvents.Add(evt);
            db.OutboxMessages.Add(outbox);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/incidents/{incident.Id}", incident.ToResponse());
        });

        group.MapGet("{id:guid}", async (Guid id, FlareDbContext db, CancellationToken ct) =>
        {
            var incident = await db.Incidents.FindAsync([id], ct);
            return incident is null
                ? Results.Problem(statusCode: 404, title: "Incident not found")
                : Results.Ok(incident.ToResponse());
        });

        group.MapPost("{id:guid}/transition", async (Guid id, TransitionRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.To))
                return Results.Problem(statusCode: 400, title: "Transition target is required");
            if (!Enum.TryParse<IncidentStatus>(req.To, ignoreCase: true, out var next))
                return Results.Problem(statusCode: 400, title: "Invalid status", detail: $"Unknown status: {req.To}");

            var incident = await db.Incidents.FindAsync([id], ct);
            if (incident is null)
                return Results.Problem(statusCode: 404, title: "Incident not found");

            try
            {
                incident.TransitionTo(next);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 422, title: "Invalid transition", detail: ex.Message);
            }

            var evt = new IncidentEvent(incident.Id, IncidentEventType.StatusChanged,
                JsonSerializer.Serialize(new { To = next.ToString() }), actorId: null);
            db.IncidentEvents.Add(evt);

            var outbox = new OutboxMessage("IncidentStatusChanged",
                JsonSerializer.Serialize(new { IncidentId = incident.Id, To = next.ToString() }));
            db.OutboxMessages.Add(outbox);

            await db.SaveChangesAsync(ct);
            return Results.Ok(incident.ToResponse());
        });

        group.MapPost("{id:guid}/roles", async (Guid id, AssignRoleRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Role))
                return Results.Problem(statusCode: 400, title: "Role is required");

            var incident = await db.Incidents.FindAsync([id], ct);
            if (incident is null)
                return Results.Problem(statusCode: 404, title: "Incident not found");

            switch (req.Role.ToUpperInvariant())
            {
                case "COMMANDER":    incident.AssignCommander(req.UserId);    break;
                case "COMMUNICATOR": incident.AssignCommunicator(req.UserId); break;
                case "RESPONDER":    incident.AssignResponder(req.UserId);    break;
                default:
                    return Results.Problem(statusCode: 400, title: "Invalid role",
                        detail: "Role must be Commander, Communicator, or Responder");
            }

            var evt = new IncidentEvent(incident.Id, IncidentEventType.RoleAssigned,
                JsonSerializer.Serialize(new { Role = req.Role, req.UserId }), actorId: null);
            db.IncidentEvents.Add(evt);

            await db.SaveChangesAsync(ct);
            return Results.Ok(incident.ToResponse());
        });

        group.MapPost("{id:guid}/events", async (Guid id, AddEventRequest req, FlareDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Type))
                return Results.Problem(statusCode: 400, title: "Event type is required");
            if (string.IsNullOrWhiteSpace(req.Payload))
                return Results.Problem(statusCode: 400, title: "Payload is required");

            if (!Enum.TryParse<IncidentEventType>(req.Type, ignoreCase: true, out var eventType))
                return Results.Problem(statusCode: 400, title: "Invalid event type", detail: $"Unknown type: {req.Type}");

            if (!AllowedExternalEventTypes.Contains(eventType))
                return Results.Problem(statusCode: 400, title: "Event type not allowed",
                    detail: $"Allowed types: {string.Join(", ", AllowedExternalEventTypes)}");

            try { using var _ = JsonDocument.Parse(req.Payload); }
            catch (JsonException)
            {
                return Results.Problem(statusCode: 400, title: "Invalid payload", detail: "Payload must be valid JSON");
            }

            var incident = await db.Incidents.FindAsync([id], ct);
            if (incident is null)
                return Results.Problem(statusCode: 404, title: "Incident not found");

            var evt = new IncidentEvent(incident.Id, eventType, req.Payload, actorId: null);
            var outbox = new OutboxMessage(
                "IncidentEventAdded",
                JsonSerializer.Serialize(new { IncidentId = id, EventId = evt.Id, Type = eventType.ToString() }));

            db.IncidentEvents.Add(evt);
            db.OutboxMessages.Add(outbox);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/incidents/{id}/events/{evt.Id}", evt.ToResponse());
        });

        group.MapGet("{id:guid}/events", async (Guid id, FlareDbContext db, CancellationToken ct) =>
        {
            var exists = await db.Incidents.AnyAsync(i => i.Id == id, ct);
            if (!exists)
                return Results.Problem(statusCode: 404, title: "Incident not found");

            var events = await db.IncidentEvents
                .Where(e => e.IncidentId == id)
                .OrderBy(e => e.CreatedAt)
                .Select(e => e.ToResponse())
                .ToListAsync(ct);

            return Results.Ok(events);
        });

        return app;
    }
}
