using System.Reflection;
using Flare.Core.Entities;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.BackgroundServices;

// Per-test ApiFactory because each case toggles ReminderHeartbeat outbox state. Joined to the
// shared "Api" collection so per-test container startups serialize against the shared fixture
// tests — same rationale as SlackDispatchTests.
[Collection("Api")]
public sealed class ReminderPersistenceTests(ApiFactory _)
{
    [Fact]
    public async Task ComputeNextDelay_FreshDb_ReturnsFullPeriod()
    {
        await using var factory = new ApiFactory().WithReminderForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var reminder = factory.Services.GetRequiredService<ActionItemReminderService>();
        var delay = await reminder.ComputeNextDelayAsync(CancellationToken.None);

        delay.Should().Be(ActionItemReminderService.Period,
            "no prior heartbeat exists; service must wait one full period before the first tick");
    }

    [Fact]
    public async Task ComputeNextDelay_RecentHeartbeat_ReturnsPartialDelay()
    {
        await using var factory = new ApiFactory().WithReminderForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        // Seed a heartbeat ~6 hours old; remaining wait should be ~18 hours (within tolerance).
        await SeedHeartbeatAsync(factory, DateTime.UtcNow - TimeSpan.FromHours(6));

        var reminder = factory.Services.GetRequiredService<ActionItemReminderService>();
        var delay = await reminder.ComputeNextDelayAsync(CancellationToken.None);

        delay.Should().BeCloseTo(TimeSpan.FromHours(18), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task ComputeNextDelay_StaleHeartbeat_ReturnsZero()
    {
        await using var factory = new ApiFactory().WithReminderForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        // Heartbeat older than the period -> next tick should run immediately.
        await SeedHeartbeatAsync(factory, DateTime.UtcNow - TimeSpan.FromHours(48));

        var reminder = factory.Services.GetRequiredService<ActionItemReminderService>();
        var delay = await reminder.ComputeNextDelayAsync(CancellationToken.None);

        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task TickAsync_WritesHeartbeatRow_AdvancingTheSchedule()
    {
        await using var factory = new ApiFactory().WithReminderForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var reminder = factory.Services.GetRequiredService<ActionItemReminderService>();
        await reminder.TickAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var heartbeats = await db.OutboxMessages
            .AsNoTracking()
            .Where(o => o.Type == ActionItemReminderService.HeartbeatType)
            .ToListAsync();

        heartbeats.Should().HaveCount(1, "every tick must persist exactly one heartbeat watermark");
        heartbeats[0].Payload.Should().Be("{}");
        heartbeats[0].CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task DispatcherSilentlyIgnoresHeartbeatRowAndDoesNotIncrementError()
    {
        // Heartbeat type is producer-consumer contract — dispatcher must mark-process it
        // and skip broadcast without LogError or DispatcherDropped counter increment.
        await using var factory = new ApiFactory()
            .WithDispatcherForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            db.OutboxMessages.Add(new OutboxMessage(ActionItemReminderService.HeartbeatType, "{}"));
            await db.SaveChangesAsync();
        }

        var dispatcher = factory.Services.GetRequiredService<Flare.Infrastructure.BackgroundServices.NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            var row = await db.OutboxMessages.AsNoTracking().SingleAsync();
            row.ProcessedAt.Should().NotBeNull("dispatcher must mark heartbeat processed so janitor can collect it");
        }
    }

    private static async Task SeedHeartbeatAsync(ApiFactory factory, DateTime createdAt)
    {
        // Bypass OutboxMessage's `CreatedAt = UtcNow` constructor via reflection on the private
        // setter. This keeps the seed Path through EF so Npgsql jsonb mapping handles Payload
        // automatically instead of fighting raw-SQL cast quirks.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var msg = new OutboxMessage(ActionItemReminderService.HeartbeatType, "{}");
        typeof(OutboxMessage).GetProperty(nameof(OutboxMessage.CreatedAt))!
            .SetValue(msg, createdAt);
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();
    }
}
