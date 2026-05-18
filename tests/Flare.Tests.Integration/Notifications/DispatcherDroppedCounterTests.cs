using System.Diagnostics.Metrics;
using Flare.Core.Entities;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Notifications;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Notifications;

// Asserts the audit-signal counter increments on the silent-drop paths. Without this guard
// a refactor that renames the "reason" tag, or removes the counter call, silently breaks
// operator dashboards that count "messages-dropped-by-dispatcher".
[Collection("Api")]
public sealed class DispatcherDroppedCounterTests(ApiFactory _)
{
    [Fact]
    public async Task UnknownOutboxType_IncrementsDispatcherDroppedWithUnknownTypeReason()
    {
        var observed = new Dictionary<string, long>();
        using var listener = BuildListener(observed);

        await using var factory = new ApiFactory().WithDispatcherForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            db.OutboxMessages.Add(new OutboxMessage("WeirdUnknownType", "{}"));
            await db.SaveChangesAsync();
        }

        var dispatcher = factory.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        observed.GetValueOrDefault("unknown_type").Should().Be(1,
            "an outbox row with no consumer route must increment the dispatcher_dropped counter with reason=unknown_type");
    }

    [Fact]
    public async Task IncidentCreatedWithMissingIncident_IncrementsDispatcherDroppedWithIncidentNotFoundReason()
    {
        var observed = new Dictionary<string, long>();
        using var listener = BuildListener(observed);

        await using var factory = new ApiFactory().WithDispatcherForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        // Seed outbox row whose IncidentId points at a deleted/never-existed incident.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            db.OutboxMessages.Add(new OutboxMessage(
                "IncidentCreated",
                System.Text.Json.JsonSerializer.Serialize(new { IncidentId = Guid.NewGuid(), Source = "manual" })));
            await db.SaveChangesAsync();
        }

        var dispatcher = factory.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        observed.GetValueOrDefault("incident_not_found").Should().Be(1);
    }

    private static MeterListener BuildListener(Dictionary<string, long> bucket)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == NotificationDiagnostics.MeterName
                    && instrument.Name == "flare_dispatcher_dropped_total")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            string? reason = null;
            foreach (var tag in tags)
                if (tag.Key == "reason") reason = tag.Value as string;
            if (reason is not null)
                bucket[reason] = bucket.GetValueOrDefault(reason) + measurement;
        });
        listener.Start();
        return listener;
    }
}
