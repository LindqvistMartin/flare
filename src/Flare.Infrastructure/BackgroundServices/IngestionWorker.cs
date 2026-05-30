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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in channelReader.ReadAllAsync(stoppingToken))
                await ProcessJobAsync(job);
        }
        catch (OperationCanceledException)
        {
            // Shutdown began. A webhook that landed in the channel microseconds before SIGTERM
            // was already answered 202 Accepted — dropping it now would lose an incident the
            // caller believes was filed. Drain whatever is already buffered before the process
            // exits. TryRead pulls only what is queued, so this terminates promptly and is
            // bounded by the host shutdown timeout.
            await DrainAsync();
        }
    }

    // Processes every job currently buffered in the channel and returns the count. Exposed as
    // internal so the shutdown drain is testable without racing the host lifecycle — same
    // pattern as NotificationDispatcher.ProcessOnceAsync and OutboxJanitorService.SweepAsync.
    internal async Task<int> DrainAsync()
    {
        var drained = 0;
        while (channelReader.TryRead(out var job))
        {
            await ProcessJobAsync(job);
            drained++;
        }
        if (drained > 0)
            logger.LogInformation("Ingestion drained {Count} buffered job(s) on shutdown", drained);
        return drained;
    }

    // No CancellationToken by design: once a job is dequeued it runs to completion. The payload
    // was fully buffered at the endpoint (IngestionJob.RawBody), so parsing does no I/O, and the
    // single SaveChanges is one transaction we never want to half-abort on a shutdown signal —
    // that is precisely the loss the drain exists to prevent. The host shutdown timeout is the
    // outer bound if a write genuinely hangs.
    private async Task ProcessJobAsync(IngestionJob job)
    {
        using var activity = ActivitySource.StartActivity($"ingest.{job.Source}");
        activity?.SetTag("source", job.Source);

        if (!_adapters.TryGetValue(job.Source, out var adapter))
        {
            logger.LogWarning("No adapter registered for source {Source}", job.Source);
            return;
        }

        try
        {
            var cmd = await adapter.ParseAsync(job, CancellationToken.None);
            if (cmd is null)
            {
                // Null means non-actionable payload (e.g. Prometheus resolve webhook with empty alerts array).
                logger.LogInformation("Adapter {Source} returned null — non-actionable payload, skipping", job.Source);
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();

            // Nullable Guid? from adapters: null means no service ID in payload.
            var serviceId = cmd.ServiceId;
            if (serviceId is null)
            {
                var fallback = await db.Services.Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
                if (fallback is null)
                {
                    logger.LogWarning("Ingestion skipped: no serviceId in payload and no services exist");
                    return;
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
            await db.SaveChangesAsync();

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
