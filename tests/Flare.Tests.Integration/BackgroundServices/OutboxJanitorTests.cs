using System.Reflection;
using Flare.Core.Entities;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Tests.Integration.BackgroundServices;

// Per-test ApiFactory because each case seeds different outbox rows with custom timestamps.
// Joined to "Api" collection to serialize per-test container startup against shared-fixture
// tests, same rationale as SlackDispatchTests and ReminderPersistenceTests.
[Collection("Api")]
public sealed class OutboxJanitorTests(ApiFactory _)
{
    [Fact]
    public async Task Sweep_DeletesProcessedRowsOlderThanRetention()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        await SeedAsync(factory,
            (OutboxMessageTypes.IncidentCreated, processedAt: DateTime.UtcNow - TimeSpan.FromDays(31)),
            (OutboxMessageTypes.IncidentCreated, processedAt: DateTime.UtcNow - TimeSpan.FromDays(31)),
            (OutboxMessageTypes.IncidentCreated, processedAt: DateTime.UtcNow - TimeSpan.FromDays(29)));

        await RunSweepAsync(factory);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var remaining = await db.OutboxMessages.AsNoTracking().ToListAsync();
        remaining.Should().HaveCount(1, "only the 29-day-old row survives the 30-day retention");
    }

    [Fact]
    public async Task Sweep_PreservesUnprocessedRowsRegardlessOfAge()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        await SeedAsync(factory,
            (OutboxMessageTypes.IncidentCreated, processedAt: null, createdAt: DateTime.UtcNow - TimeSpan.FromDays(60)),
            (OutboxMessageTypes.IncidentCreated, processedAt: DateTime.UtcNow - TimeSpan.FromDays(31), createdAt: null));

        await RunSweepAsync(factory);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var remaining = await db.OutboxMessages.AsNoTracking().ToListAsync();
        remaining.Should().HaveCount(1, "unprocessed row stays even though it's 60 days old; processed-and-old row is purged");
        remaining[0].ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Sweep_PreservesMostRecentHeartbeatEvenIfStale()
    {
        // Critical: a >30-day outage must not erase the last heartbeat, otherwise the reminder
        // service's ComputeNextDelayAsync would see no watermark and wait a full Period from
        // process start instead of resuming the schedule.
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        await SeedAsync(factory,
            (ActionItemReminderService.HeartbeatType, processedAt: DateTime.UtcNow - TimeSpan.FromDays(60), createdAt: DateTime.UtcNow - TimeSpan.FromDays(60)));

        await RunSweepAsync(factory);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var heartbeats = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == ActionItemReminderService.HeartbeatType)
            .ToListAsync();
        heartbeats.Should().HaveCount(1, "the only heartbeat row must survive regardless of age");
    }

    [Fact]
    public async Task Sweep_PreservesOnlyLatestHeartbeatWhenMultipleAreStale()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var olderTime = DateTime.UtcNow - TimeSpan.FromDays(60);
        var newerTime = DateTime.UtcNow - TimeSpan.FromDays(45);
        await SeedAsync(factory,
            (ActionItemReminderService.HeartbeatType, processedAt: olderTime, createdAt: olderTime),
            (ActionItemReminderService.HeartbeatType, processedAt: newerTime, createdAt: newerTime));

        await RunSweepAsync(factory);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var heartbeats = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == ActionItemReminderService.HeartbeatType)
            .ToListAsync();
        heartbeats.Should().HaveCount(1, "only the most-recent heartbeat is preserved; older one is sweepable");
        heartbeats[0].CreatedAt.Should().BeCloseTo(newerTime, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Sweep_EmptyTable_IsNoOp()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var act = () => RunSweepAsync(factory);
        await act.Should().NotThrowAsync();
    }

    private static async Task SeedAsync(ApiFactory factory,
        params (string type, DateTime? processedAt, DateTime? createdAt)[] rows)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var createdAtProp = typeof(OutboxMessage).GetProperty(nameof(OutboxMessage.CreatedAt))!;
        var processedAtProp = typeof(OutboxMessage).GetProperty(nameof(OutboxMessage.ProcessedAt))!;
        foreach (var (type, processedAt, createdAt) in rows)
        {
            var msg = new OutboxMessage(type, "{}");
            if (createdAt.HasValue) createdAtProp.SetValue(msg, createdAt.Value);
            else if (processedAt.HasValue) createdAtProp.SetValue(msg, processedAt.Value);
            if (processedAt.HasValue) processedAtProp.SetValue(msg, processedAt);
            db.OutboxMessages.Add(msg);
        }
        await db.SaveChangesAsync();
    }

    // Overload for terse default-createdAt usage.
    private static Task SeedAsync(ApiFactory factory, params (string type, DateTime? processedAt)[] rows)
    {
        var withCreatedAt = rows.Select(r => (r.type, r.processedAt, (DateTime?)r.processedAt)).ToArray();
        return SeedAsync(factory, withCreatedAt);
    }

    private static async Task RunSweepAsync(ApiFactory factory)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var janitor = new OutboxJanitorService(scopeFactory, NullLogger<OutboxJanitorService>.Instance);
        await janitor.SweepAsync(CancellationToken.None);
    }
}
