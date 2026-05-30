using System.Net;
using System.Net.Http.Json;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

// Readiness probe contract: Postgres is the hard gate (503 when down), outbox lag is a
// non-gating degraded signal (still 200 so the load balancer keeps serving), realtime reports
// the SignalR plane is wired. The database-down 503 path is not exercised here — tearing the
// Testcontainers Postgres mid-test to assert it is more fragile than the signal is worth.
[Collection("Api")]
public sealed class ReadinessProbeTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ready_WithNoOutboxBacklog_ReportsReady()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz/ready");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await resp.Content.ReadFromJsonAsync<ReadyBody>())!;
        body.Status.Should().Be("ready");
        body.Database.Should().Be("up");
        body.Realtime.Should().Be("up");
        body.Outbox.Degraded.Should().BeFalse();
        body.Outbox.UnprocessedLagSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Ready_WithStaleUnprocessedOutboxRow_ReportsDegradedButStill200()
    {
        await SeedUnprocessedOutboxAsync(factory, ageMinutes: 5);
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz/ready");

        // Degraded is intentionally still 200 — the API serves fine, only the dispatcher is
        // behind, so the instance must stay in rotation while the backlog is visible to monitoring.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await resp.Content.ReadFromJsonAsync<ReadyBody>())!;
        body.Status.Should().Be("degraded");
        body.Database.Should().Be("up");
        body.Outbox.Degraded.Should().BeTrue();
        body.Outbox.UnprocessedLagSeconds.Should().BeGreaterThan(60);
    }

    private static async Task SeedUnprocessedOutboxAsync(ApiFactory factory, int ageMinutes)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var msg = new OutboxMessage(OutboxMessageTypes.IncidentCreated, "{}");
        // CreatedAt has a private setter; back-date it so the endpoint computes a lag over the
        // degraded threshold. ProcessedAt stays null — the hosted dispatcher is removed in tests,
        // so nothing drains the row before the probe reads it.
        typeof(OutboxMessage).GetProperty(nameof(OutboxMessage.CreatedAt))!
            .SetValue(msg, DateTime.UtcNow - TimeSpan.FromMinutes(ageMinutes));
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();
    }

    private sealed record ReadyBody(string Status, string Database, string Realtime, OutboxBody Outbox);
    private sealed record OutboxBody(long UnprocessedLagSeconds, bool Degraded);
}
