using System.Threading.Channels;
using Flare.Core.Abstractions;
using Flare.Core.Entities;
using Flare.Core.Workers;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Tests.Integration.BackgroundServices;

// Per-test ApiFactory so each case gets a fresh ingestion channel — a shared singleton channel
// would let buffered jobs leak across tests. Joined to "Api" to serialize container startup,
// same rationale as OutboxJanitorTests.
[Collection("Api")]
public sealed class IngestionDrainTests(ApiFactory _)
{
    [Fact]
    public async Task Drain_TurnsEveryBufferedJobIntoAnIncident()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();
        await SeedServiceAsync(factory);

        // Buffer three webhook jobs without the worker running — in tests the hosted
        // IngestionWorker is removed (ApiFactory.RemoveHosted), so nothing consumes them until
        // the drain we drive below. This is the shutdown state: jobs queued, process stopping.
        var writer = factory.Services.GetRequiredService<ChannelWriter<IngestionJob>>();
        for (var i = 0; i < 3; i++)
            writer.TryWrite(new IngestionJob("generic", $$"""{"title":"Buffered alert {{i}}"}""", EmptyHeaders))
                .Should().BeTrue("the bounded channel has ample room for three jobs");

        var drained = await NewWorker(factory).DrainAsync();

        drained.Should().Be(3, "every buffered job must be processed on shutdown, not dropped");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var incidents = await db.Incidents.AsNoTracking().ToListAsync();
        incidents.Should().HaveCount(3, "the drained jobs became incidents in the database");
    }

    [Fact]
    public async Task Drain_OnEmptyChannel_IsNoOp()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var drained = await NewWorker(factory).DrainAsync();

        drained.Should().Be(0);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>();

    private static IngestionWorker NewWorker(ApiFactory factory) => new(
        factory.Services.GetRequiredService<ChannelReader<IngestionJob>>(),
        factory.Services.GetServices<IAlertIngestionAdapter>(),
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<IngestionWorker>.Instance);

    // The generic adapter emits no serviceId, so the worker falls back to the first service in
    // the database — seed exactly one so that fallback is deterministic.
    private static async Task SeedServiceAsync(ApiFactory factory)
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
    }
}
