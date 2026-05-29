using Flare.Core.Entities;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.BackgroundServices;

// Exercises MetricsAggregator.RefreshAsync via internal exposure — proves the production
// REFRESH path produces the matview rows the /api/v1/metrics/* endpoints rely on, without
// waiting for the 5-minute poll loop. Mirrors the SlackDispatchTests pattern: own ApiFactory
// per test, ConfigureWebHost flag swaps the hosted-service registration for a singleton, and
// the test resolves and ticks directly.
[Collection("Api")]
public sealed class MetricsAggregatorTests(ApiFactory _)
{
    [Fact]
    public async Task RefreshAsync_AfterIncidentSeed_PopulatesMatviews()
    {
        await using var factory = new ApiFactory().WithMetricsAggregatorForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        // Seed one resolved incident with deterministic CreatedAt / ResolvedAt / AcknowledgedAt
        // so both matviews have a known row to assert after RefreshAsync. The matview-math is
        // separately pinned by MetricsAggregateTests; this test pins the production refresh path.
        var serviceId = await SeedSingleIncidentAsync(factory,
            resolveDurationMs: 1_200_000,
            ackDurationMs: 120_000,
            createdDaysAgo: 1);

        var aggregator = factory.Services.GetRequiredService<MetricsAggregator>();
        await aggregator.RefreshAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var mttr = await db.MttrByService30d.SingleAsync(r => r.ServiceId == serviceId);
        mttr.IncidentCount.Should().Be(1);
        mttr.AvgMttrMs.Should().Be(1_200_000);
        mttr.P50MttrMs.Should().Be(1_200_000);

        var mtta = await db.MttaByService30d.SingleAsync(r => r.ServiceId == serviceId);
        mtta.IncidentCount.Should().Be(1);
        mtta.AvgMttaMs.Should().Be(120_000);
        mtta.P50MttaMs.Should().Be(120_000);
    }

    private static async Task<Guid> SeedSingleIncidentAsync(
        ApiFactory factory, long resolveDurationMs, long ackDurationMs, int createdDaysAgo)
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

        var created = DateTime.UtcNow - TimeSpan.FromDays(createdDaysAgo);
        var resolved = created + TimeSpan.FromMilliseconds(resolveDurationMs);
        var acknowledged = created + TimeSpan.FromMilliseconds(ackDurationMs);

        var incident = new Incident(svc.Id, "Aggregator test incident", IncidentSeverity.Sev2);
        typeof(Incident).GetProperty(nameof(Incident.CreatedAt))!.SetValue(incident, created);
        typeof(Incident).GetProperty(nameof(Incident.ResolvedAt))!.SetValue(incident, (DateTime?)resolved);
        typeof(Incident).GetProperty(nameof(Incident.AcknowledgedAt))!.SetValue(incident, (DateTime?)acknowledged);
        typeof(Incident).GetProperty(nameof(Incident.Status))!.SetValue(incident, IncidentStatus.Resolved);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        return svc.Id;
    }
}
