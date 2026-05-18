using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Flare.Core.Abstractions;
using Flare.Core.Entities;
using Flare.Core.Workers;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.Infrastructure.BackgroundServices;

public sealed class IngestionWorker(
    ChannelReader<IngestionJob> channelReader,
    IEnumerable<IAlertIngestionAdapter> adapters,
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Flare.Ingestion");

    private readonly IReadOnlyDictionary<string, IAlertIngestionAdapter> _adapters =
        adapters.ToDictionary(a => a.Source, StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var job in channelReader.ReadAllAsync(ct))
        {
            using var activity = ActivitySource.StartActivity($"ingest.{job.Source}");
            activity?.SetTag("source", job.Source);

            if (!_adapters.TryGetValue(job.Source, out var adapter))
            {
                logger.LogWarning("No adapter registered for source {Source}", job.Source);
                continue;
            }

            try
            {
                var cmd = await adapter.ParseAsync(job, ct);
                if (cmd is null)
                {
                    // Null means non-actionable payload (e.g. Prometheus resolve webhook with empty alerts array).
                    logger.LogInformation("Adapter {Source} returned null — non-actionable payload, skipping", job.Source);
                    continue;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

                // Nullable Guid? from adapters: null means no service ID in payload.
                var serviceId = cmd.ServiceId;
                if (serviceId is null)
                {
                    var fallback = await db.Services.Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
                    if (fallback is null)
                    {
                        logger.LogWarning("Ingestion skipped: no serviceId in payload and no services exist");
                        continue;
                    }
                    serviceId = fallback.Value;
                }

                var incident = new Incident(serviceId.Value, cmd.Title, cmd.Severity);
                var evt = new IncidentEvent(
                    incident.Id,
                    IncidentEventType.Created,
                    JsonSerializer.Serialize(new { cmd.Title, Severity = cmd.Severity.ToString(), Source = job.Source }),
                    actorId: null);
                var outbox = new OutboxMessage(
                    OutboxMessageTypes.IncidentCreated,
                    JsonSerializer.Serialize(new { IncidentId = incident.Id, Source = job.Source }));

                db.Incidents.Add(incident);
                db.IncidentEvents.Add(evt);
                db.OutboxMessages.Add(outbox);
                await db.SaveChangesAsync(ct);

                activity?.SetTag("incidentId", incident.Id.ToString());
                logger.LogInformation("Incident {Id} created via {Source}", incident.Id, job.Source);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogWarning(ex, "Failed to process ingestion job from {Source}", job.Source);
            }
        }
    }
}
