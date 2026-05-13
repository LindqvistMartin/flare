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

            if (!channelWriter.TryWrite(job))
            {
                logger.LogWarning("Ingestion channel is full, dropping webhook from {Source}", source);
                return Results.StatusCode(503);
            }

            return Results.Accepted();
        });

        return app;
    }
}
