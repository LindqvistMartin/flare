using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flare.Tests.Integration.Database;

[Collection("Api")]
public sealed class AppendOnlyConstraintTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpdateIncidentEventViaRawSql_ThrowsPostgresException()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var (evtId, originalActor) = await SeedAsync(db);

        var act = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "IncidentEvents" SET "ActorId" = '00000000-0000-0000-0000-000000000001' WHERE "Id" = {evtId}""");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.MessageText.Contains("incident_events is append-only"));

        var actorAfter = await db.IncidentEvents
            .AsNoTracking()
            .Where(e => e.Id == evtId)
            .Select(e => e.ActorId)
            .SingleAsync();
        actorAfter.Should().Be(originalActor);
    }

    [Fact]
    public async Task DeleteIncidentEventViaRawSql_ThrowsPostgresException()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var (evtId, _) = await SeedAsync(db);

        var act = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "IncidentEvents" WHERE "Id" = {evtId}""");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.MessageText.Contains("incident_events is append-only"));

        var exists = await db.IncidentEvents.AsNoTracking().AnyAsync(e => e.Id == evtId);
        exists.Should().BeTrue();
    }

    private static async Task<(Guid EventId, Guid? OriginalActor)> SeedAsync(FlareDbContext db)
    {
        var org = new Organization("Test Org", "test-org");
        var team = new Team(org.Id, "Test Team", "test-team");
        var svc = new Service(team.Id, "Payment API");
        var incident = new Incident(svc.Id, "Trigger smoke", IncidentSeverity.Sev3);
        var evt = new IncidentEvent(incident.Id, IncidentEventType.Created, "{}", actorId: null);

        db.Organizations.Add(org);
        db.Teams.Add(team);
        db.Services.Add(svc);
        db.Incidents.Add(incident);
        db.IncidentEvents.Add(evt);
        await db.SaveChangesAsync();

        return (evt.Id, evt.ActorId);
    }
}
