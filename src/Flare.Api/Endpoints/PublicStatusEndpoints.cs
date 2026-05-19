using System.Text.Json;
using System.Text.RegularExpressions;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Core.Sanitization;
using Flare.Infrastructure.Caching;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flare.Api.Endpoints;

public static class PublicStatusEndpoints
{
    private const int LookbackDays = 30;

    // Same shape the entity ctor enforces. Checking here too keeps malformed input from
    // reaching the DB query — `/public/status/!!!` rejects before any work happens.
    private static readonly Regex SlugShape = new(
        "^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",
        RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapPublicStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered outside any /api/v1 group: stable, unauthenticated, future auth on
        // /api/v1/* must not gate the public surface.
        app.MapGet("/public/status/{slug}", async (
            string slug,
            FlareDbContext db,
            IStatusPageCache cache,
            CancellationToken ct) =>
        {
            var normalized = slug.Trim().ToLowerInvariant();
            // Same 404 shape for invalid syntax and missing slug — do not let an enumerator
            // distinguish "doesn't exist" from "malformed".
            if (normalized.Length is < 1 or > StatusPage.MaxSlugLength) return SlugNotFound();
            if (!SlugShape.IsMatch(normalized)) return SlugNotFound();

            // Cache handles single-flight (concurrent misses share one DB load) and version
            // tracking (drops the loaded snapshot if an invalidation fires during the load).
            var json = await cache.TryGetOrLoadAsync(normalized,
                async cancellationToken => await LoadAsync(db, normalized, cancellationToken),
                ct);

            if (json is null) return SlugNotFound();

            return Results.Content(json, "application/json");
        });

        return app;
    }

    private static IResult SlugNotFound() =>
        Results.Problem(statusCode: 404, title: "Status page not found");

    private static async Task<StatusPageLoad?> LoadAsync(FlareDbContext db, string slug, CancellationToken ct)
    {
        var page = await db.StatusPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (page is null) return null;

        var serviceIds = page.GetServiceIds();
        var since = DateTime.UtcNow.AddDays(-LookbackDays);

        var services = await db.Services
            .AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        var incidents = await db.Incidents
            .AsNoTracking()
            .Where(i => serviceIds.Contains(i.ServiceId) && i.CreatedAt >= since)
            .Select(i => new
            {
                i.ServiceId,
                i.Title,
                i.Severity,
                i.Status,
                i.CreatedAt,
            })
            .ToListAsync(ct);

        // Operator-supplied strings (service name, page title/description) and
        // attacker-controllable strings (incident title from webhook payloads) flow verbatim
        // into a public unauthenticated JSON surface. The notification path already sanitises
        // these — the public path is a higher-trust target than chat (third-party embeds,
        // screen readers, search indexers) and must do the same.
        var serviceStatuses = services
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                var serviceIncidents = incidents.Where(i => i.ServiceId == s.Id).ToList();
                var active = serviceIncidents
                    .Where(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed)
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => new PublicActiveIncidentResponse(
                        PayloadSanitizer.Sanitize(i.Title),
                        i.Severity.ToString(),
                        i.Status.ToString(),
                        i.CreatedAt))
                    .ToList();
                return new PublicServiceStatusResponse(
                    PayloadSanitizer.Sanitize(s.Name),
                    DeriveStatus(active),
                    active,
                    serviceIncidents.Count);
            })
            .ToList();

        var response = new PublicStatusResponse(
            page.Slug,
            PayloadSanitizer.Sanitize(page.Title),
            page.Description is null ? null : PayloadSanitizer.Sanitize(page.Description),
            DeriveOverall(serviceStatuses),
            DateTime.UtcNow,
            serviceStatuses);

        return new StatusPageLoad(JsonSerializer.Serialize(response), serviceIds);
    }

    // Sev1 active → "major"; Sev2 active → "degraded"; Sev3/4 active surface in the list
    // but do not flip the headline — operators treat Sev3 as "known, not page-worthy".
    private static string DeriveStatus(IReadOnlyList<PublicActiveIncidentResponse> active)
    {
        if (active.Any(i => i.Severity == nameof(IncidentSeverity.Sev1))) return "major";
        if (active.Any(i => i.Severity == nameof(IncidentSeverity.Sev2))) return "degraded";
        return "operational";
    }

    private static string DeriveOverall(IReadOnlyList<PublicServiceStatusResponse> services)
    {
        // A page whose services were all deleted (or one that never had any) is not actually
        // "operational" — we have nothing to report on. Returning a distinct status lets the
        // page surface communicate "no data" instead of falsely advertising green.
        if (services.Count == 0) return "unknown";
        if (services.Any(s => s.Status == "major")) return "major";
        if (services.Any(s => s.Status == "degraded")) return "degraded";
        return "operational";
    }
}
