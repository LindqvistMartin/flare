using System.Net.Http.Json;
using System.Reflection;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

// Pins the matview math. Seeds three resolved incidents with deterministic
// durations, refreshes mttr_by_service_30d, and asserts /api/v1/metrics/mttr
// returns the expected average + count. Backs up ADR-004's claim that the
// matview-based aggregation actually agrees with the underlying data.
[Collection("Api")]
public sealed class MetricsAggregateTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetMttr_AfterRefresh_ReturnsCorrectAverageAndCount()
    {
        // Three incidents on one service with resolve durations of 10, 20, 30 minutes
        // → AvgMttrMs = 1_200_000, P50MttrMs = 1_200_000 (the middle sample),
        // IncidentCount = 3.
        var serviceId = await SeedIncidentsAsync(
            durationsMs: [600_000, 1_200_000, 1_800_000],
            createdDaysAgo: [1, 2, 3]);

        await RefreshMatviewsAsync();

        var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<List<ServiceMttrResponse>>("/api/v1/metrics/mttr");
        rows.Should().NotBeNull();

        var row = rows!.Single(r => r.ServiceId == serviceId);
        row.IncidentCount.Should().Be(3);
        row.AvgMttrMs.Should().Be(1_200_000);
        // PERCENTILE_CONT on three equally-spaced samples (600k / 1200k / 1800k)
        // returns the middle one. Asserting strict equality locks in the contract;
        // any drift (e.g. window expansion) shows up here loud and fast.
        row.P50MttrMs.Should().Be(1_200_000);
    }

    [Fact]
    public async Task GetMttr_WithNoResolvedIncidents_ReturnsServiceRowWithZeroes()
    {
        // LEFT JOIN + COALESCE(..., 0) guarantees every service shows up even
        // when no resolved incidents fall in the 30-day window — important for
        // the dashboard query, which would otherwise silently drop the service.
        var serviceId = await SeedServiceOnlyAsync();
        await RefreshMatviewsAsync();

        var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<List<ServiceMttrResponse>>("/api/v1/metrics/mttr");

        var row = rows!.Single(r => r.ServiceId == serviceId);
        row.IncidentCount.Should().Be(0);
        row.AvgMttrMs.Should().Be(0);
        row.P50MttrMs.Should().Be(0);
    }

    [Fact]
    public async Task GetMtta_AfterRefresh_ReturnsCorrectAverageAndCount()
    {
        // Mirror of the MTTR test for the acknowledgement-time matview. Three
        // incidents on one service with ack gaps of 1, 2, 3 minutes
        // → AvgMttaMs = 120_000, P50MttaMs = 120_000, IncidentCount = 3.
        var serviceId = await SeedIncidentsAsync(
            durationsMs: [600_000, 1_200_000, 1_800_000],
            createdDaysAgo: [1, 2, 3],
            ackDurationsMs: [60_000, 120_000, 180_000]);

        await RefreshMatviewsAsync();

        var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<List<ServiceMttaResponse>>("/api/v1/metrics/mtta");
        rows.Should().NotBeNull();

        var row = rows!.Single(r => r.ServiceId == serviceId);
        row.IncidentCount.Should().Be(3);
        row.AvgMttaMs.Should().Be(120_000);
        row.P50MttaMs.Should().Be(120_000);
    }

    [Fact]
    public async Task GetDashboard_ReturnsOpenCountsAndMttrAverage()
    {
        // Compose all three dashboard signals in one fixture: 2 active incidents
        // (Triggered + Investigating), 3 resolved incidents on the same service
        // for the MTTR weighting, 1 overdue action item, 1 done action item with
        // a past due date that must NOT count. The endpoint reads from three
        // different tables + a matview; a comprehensive test catches drift on
        // any of them in one round-trip.
        var serviceId = await SeedIncidentsAsync(
            durationsMs: [600_000, 1_200_000, 1_800_000],
            createdDaysAgo: [1, 2, 3]);
        await SeedOpenIncidentsAsync(serviceId, IncidentStatus.Triggered, IncidentStatus.Investigating);
        await SeedActionItemsAsync(serviceId,
            (DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), ActionItemStatus.Open),    // overdue
            (DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2), ActionItemStatus.Done));   // past due but done → skip
        await RefreshMatviewsAsync();

        var client = factory.CreateClient();
        var dashboard = await client.GetFromJsonAsync<DashboardResponse>("/api/v1/metrics/dashboard");

        dashboard.Should().NotBeNull();
        dashboard!.OpenIncidentsCount.Should().Be(2);
        dashboard.OverdueActionItemsCount.Should().Be(1);
        dashboard.MttrLast30dAvgMs.Should().Be(1_200_000);
    }

    private async Task<Guid> SeedIncidentsAsync(
        long[] durationsMs,
        int[] createdDaysAgo,
        long[]? ackDurationsMs = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var org = new Organization("Test Org", "test-org");
        var team = new Team(org.Id, "Test Team", "test-team");
        var svc = new Service(team.Id, "Payment API");
        db.Organizations.Add(org);
        db.Teams.Add(team);
        db.Services.Add(svc);
        await db.SaveChangesAsync();

        var createdAtProp = typeof(Incident).GetProperty(nameof(Incident.CreatedAt))!;
        var resolvedAtProp = typeof(Incident).GetProperty(nameof(Incident.ResolvedAt))!;
        var acknowledgedAtProp = typeof(Incident).GetProperty(nameof(Incident.AcknowledgedAt))!;
        var statusProp = typeof(Incident).GetProperty(nameof(Incident.Status))!;

        for (var i = 0; i < durationsMs.Length; i++)
        {
            var created = DateTime.UtcNow - TimeSpan.FromDays(createdDaysAgo[i]);
            var resolved = created + TimeSpan.FromMilliseconds(durationsMs[i]);
            var incident = new Incident(svc.Id, $"Test incident {i}", IncidentSeverity.Sev2);
            // Bypass the state machine to set Status / ResolvedAt directly — the matview
            // reads aggregate-root timestamps, not the event stream (see ADR-004).
            createdAtProp.SetValue(incident, created);
            resolvedAtProp.SetValue(incident, (DateTime?)resolved);
            statusProp.SetValue(incident, IncidentStatus.Resolved);
            if (ackDurationsMs is not null)
            {
                var acknowledged = created + TimeSpan.FromMilliseconds(ackDurationsMs[i]);
                acknowledgedAtProp.SetValue(incident, (DateTime?)acknowledged);
            }
            db.Incidents.Add(incident);
        }
        await db.SaveChangesAsync();
        return svc.Id;
    }

    private async Task SeedOpenIncidentsAsync(Guid serviceId, params IncidentStatus[] statuses)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var statusProp = typeof(Incident).GetProperty(nameof(Incident.Status))!;
        for (var i = 0; i < statuses.Length; i++)
        {
            var incident = new Incident(serviceId, $"Open incident {i}", IncidentSeverity.Sev3);
            statusProp.SetValue(incident, statuses[i]);
            db.Incidents.Add(incident);
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedActionItemsAsync(Guid serviceId, params (DateOnly DueDate, ActionItemStatus Status)[] items)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        // Action items hang off a postmortem, which hangs off a resolved incident.
        // Reuse one such incident from the seeded set; if none — bail loudly so the
        // test fixture stays honest about its preconditions.
        var resolvedIncident = await db.Incidents
            .Where(i => i.ServiceId == serviceId && i.Status == IncidentStatus.Resolved)
            .FirstAsync();

        var postmortem = new Postmortem(resolvedIncident.Id, impact: "test impact", timeline: "[]", rootCause: "");
        db.Postmortems.Add(postmortem);
        await db.SaveChangesAsync();

        foreach (var (dueDate, status) in items)
        {
            var item = new ActionItem(postmortem.Id, $"Item {Guid.NewGuid()}", dueDate: dueDate);
            if (status != ActionItemStatus.Open)
                item.Transition(status);
            db.ActionItems.Add(item);
        }
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedServiceOnlyAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

        var org = new Organization("Test Org", "test-org");
        var team = new Team(org.Id, "Test Team", "test-team");
        var svc = new Service(team.Id, "Auth Service");
        db.Organizations.Add(org);
        db.Teams.Add(team);
        db.Services.Add(svc);
        await db.SaveChangesAsync();
        return svc.Id;
    }

    private async Task RefreshMatviewsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        // Mirror MetricsAggregator's refresh path. CONCURRENTLY needs the unique
        // index that lives in the matview migration, which is present.
        await db.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mttr_by_service_30d;");
        await db.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mtta_by_service_30d;");
    }
}
