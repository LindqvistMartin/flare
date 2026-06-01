using System.Threading.Channels;
using Flare.Core.Workers;
using Microsoft.Extensions.Logging;

namespace Flare.Api.Endpoints;

public static class WebhooksEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/ingest/{source}", async (
            string source,
            HttpContext ctx,
            ChannelWriter<IngestionJob> channelWriter,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Flare.Api.Webhooks");

            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);

            var headers = ctx.Request.Headers
                .Where(h => !string.IsNullOrEmpty(h.Key))
                .ToDictionary(h => h.Key, h => h.Value.ToString());

            var job = new IngestionJob(source, body, headers);

            // TryWrite returns false when the bounded channel is full (FullMode.Wait). Surface that
            // as 503 backpressure so the sender retries — never a 202 for a job we could not enqueue.
            if (!channelWriter.TryWrite(job))
            {
                logger.LogWarning("Ingestion channel is full, rejecting webhook from {Source} with 503", source);
                return Results.StatusCode(503);
            }

            return Results.Accepted();
        });

        return app;
    }
}
