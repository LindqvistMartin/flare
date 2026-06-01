using System.Net;
using System.Text;
using System.Threading.Channels;
using Flare.Core.Workers;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

// Per-test ApiFactory for a fresh ingestion channel — this test fills it to capacity, which would
// leak into siblings on a shared singleton channel. Joined to "Api" to serialize container startup.
[Collection("Api")]
public sealed class WebhookBackpressureTests(ApiFactory _)
{
    [Fact]
    public async Task Ingest_WhenChannelFull_Returns503_NotSilent202()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        // Fill the bounded channel to capacity. The hosted IngestionWorker is removed in tests, so
        // nothing drains it. With FullMode.Wait, TryWrite returns false once full — so the endpoint
        // must answer 503 (backpressure), not the silent 202-then-drop that DropWrite produced.
        var writer = factory.Services.GetRequiredService<ChannelWriter<IngestionJob>>();
        var accepted = 0;
        while (writer.TryWrite(new IngestionJob("generic", "{}", EmptyHeaders)))
            accepted++;
        accepted.Should().Be(500, "the ingestion channel is bounded at 500");

        var client = factory.CreateClient();
        var resp = await client.PostAsync(
            "/api/v1/webhooks/ingest/generic",
            new StringContent("""{"title":"overflow"}""", Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "a full channel must surface backpressure, never accept a job it cannot enqueue");
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>();
}
