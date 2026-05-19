using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Core.Sanitization;
using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

// Per-test factory: the cache (and the manual-tick dispatcher) is process state, so the
// cache-bust and dispatcher-tick scenarios need a fresh app per case to avoid bleeding into
// each other. [Collection("Api")] still serializes container starts against the shared tests.
[Collection("Api")]
public sealed class PublicStatusPageTests(ApiFactory _)
{
    [Fact]
    public async Task GetPublicStatus_ReturnsActiveIncidentAnd30DayCount()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (orgId, serviceId) = await SeedOrgAndServiceAsync(factory);
        await SeedActiveIncidentAsync(factory, serviceId, "Latency spike on /charge", IncidentSeverity.Sev2);
        await SeedResolvedIncidentAsync(factory, serviceId, "Old issue", IncidentSeverity.Sev3);

        var client = factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", "Customer-facing services.",
                [serviceId]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var publicResponse = await client.GetAsync("/public/status/acme");
        publicResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await publicResponse.Content.ReadFromJsonAsync<PublicStatusResponse>();
        body.Should().NotBeNull();
        body!.Slug.Should().Be("acme");
        body.Title.Should().Be("Acme Status");
        body.OverallStatus.Should().Be("degraded");
        body.Services.Should().ContainSingle();
        var svc = body.Services[0];
        svc.Name.Should().Be("Payment API");
        svc.Status.Should().Be("degraded");
        svc.IncidentsLast30Days.Should().Be(2);
        svc.ActiveIncidents.Should().ContainSingle();
        svc.ActiveIncidents[0].Title.Should().Be("Latency spike on /charge");
        svc.ActiveIncidents[0].Severity.Should().Be("Sev2");
        svc.ActiveIncidents[0].Status.Should().Be("Investigating");
    }

    [Fact]
    public async Task GetPublicStatus_NormalizesUppercaseSlug()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (orgId, serviceId) = await SeedOrgAndServiceAsync(factory);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));

        var response = await client.GetAsync("/public/status/ACME");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPublicStatus_SanitizesHostileStringsInResponse()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (orgId, serviceId) = await SeedOrgAndServiceAsync(factory);
        // Hostile incident title: bidi RTL override, embedded angle brackets that would
        // render as HTML in a third-party embed, plus a BEL control char that would split
        // log lines downstream. Status page is unauthenticated and frequently embedded.
        // Numeric \u escapes keep this source file ASCII-safe (Session 6 convention).
        const string bidiOverride = "\u202E";
        const string bel = "\u0007";
        var hostileTitle = $"Latency<script>alert(1)</script>{bidiOverride}{bel}spike";
        await SeedActiveIncidentAsync(factory, serviceId, hostileTitle, IncidentSeverity.Sev2);

        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme<b>Status</b>",
                "Customer<svg/onload=1>services", [serviceId]));

        var body = await GetPublicAsync(client, "acme");

        // Strong assertion: the public output must equal the canonical sanitizer output for
        // each field. A future change that strips angle brackets via some unrelated code
        // path would have satisfied the looser NotContain checks while losing bidi/control
        // protection; this contract pins the actual sanitiser in place.
        body.Title.Should().Be(PayloadSanitizer.Sanitize("Acme<b>Status</b>"));
        body.Description.Should().NotBeNull();
        body.Description!.Should().Be(PayloadSanitizer.Sanitize("Customer<svg/onload=1>services"));
        var incident = body.Services[0].ActiveIncidents[0];
        incident.Title.Should().Be(PayloadSanitizer.Sanitize(hostileTitle));
        body.Services[0].Name.Should().Be(PayloadSanitizer.Sanitize("Payment API"));
    }

    [Theory]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 65 chars
    [InlineData("..")]
    [InlineData("%2e%2e")]
    public async Task GetPublicStatus_OutOfBoundsSlug_Returns404(string slug)
    {
        // 65 chars exceeds MaxSlugLength; ".." and its URL-encoded form fail the regex.
        // Same 404 shape so an enumerator cannot tell which gate rejected.
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var client = factory.CreateClient();
        var encoded = Uri.EscapeDataString(slug);
        var response = await client.GetAsync($"/public/status/{encoded}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme!")]
    [InlineData("ac me")]
    public async Task GetPublicStatus_MalformedSlug_Returns404WithoutHittingDb(string malformed)
    {
        // Slug shape must be rejected before the DB query — otherwise an attacker can
        // enumerate the table with a high-rate `/public/status/!!!{rand}` loop. Same 404
        // shape as "slug not found" so the response does not leak which side rejected.
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var client = factory.CreateClient();
        var encoded = Uri.EscapeDataString(malformed);
        var response = await client.GetAsync($"/public/status/{encoded}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPublicStatus_AllServicesDeleted_ReturnsUnknownOverall()
    {
        // A page whose underlying services have all been deleted (ServiceIds in jsonb have
        // no FK enforcement) cannot honestly report "operational". Returning "unknown"
        // signals to the embedding surface that the page has no data to show.
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (orgId, serviceId) = await SeedOrgAndServiceAsync(factory);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            var svc = await db.Services.FindAsync(serviceId);
            db.Services.Remove(svc!);
            await db.SaveChangesAsync();
        }

        var body = await GetPublicAsync(client, "acme");

        body.OverallStatus.Should().Be("unknown");
        body.Services.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPublicStatus_UnknownSlug_Returns404()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var client = factory.CreateClient();
        var response = await client.GetAsync("/public/status/missing");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPublicStatus_AfterDirectDbInsert_StillServesCachedResponse()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (orgId, serviceId) = await SeedOrgAndServiceAsync(factory);
        await SeedActiveIncidentAsync(factory, serviceId, "First incident", IncidentSeverity.Sev2);

        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));

        var first = await GetPublicAsync(client, "acme");

        // Bypass the API so no outbox row is written and the dispatcher cannot invalidate.
        await SeedActiveIncidentAsync(factory, serviceId, "Second incident", IncidentSeverity.Sev1);

        var second = await GetPublicAsync(client, "acme");

        second.Services[0].ActiveIncidents.Should().HaveCount(first.Services[0].ActiveIncidents.Count,
            "cache hit must hide the direct DB insert that did not write an outbox event");
        second.OverallStatus.Should().Be(first.OverallStatus);
    }

    [Fact]
    public async Task IncidentStateChange_ViaDispatcher_InvalidatesPublicStatusCache()
    {
        await using var factory = new ApiFactory().WithDispatcherForManualTick();
        await factory.InitializeAsync();
        await factory.CleanAsync();

        var (orgId, serviceId) = await SeedOrgAndServiceAsync(factory);
        var incidentId = await SeedActiveIncidentAsync(factory, serviceId, "Latency spike", IncidentSeverity.Sev2);

        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));

        var primed = await GetPublicAsync(client, "acme");
        primed.Services[0].ActiveIncidents.Should().ContainSingle();

        var transitionResponse = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentId}/transition", new { To = nameof(IncidentStatus.Resolved) });
        transitionResponse.IsSuccessStatusCode.Should()
            .BeTrue($"transition to Resolved returned {transitionResponse.StatusCode}");

        var dispatcher = factory.Services.GetRequiredService<NotificationDispatcher>();
        await dispatcher.ProcessOnceAsync(CancellationToken.None);

        var afterResolve = await GetPublicAsync(client, "acme");
        afterResolve.Services[0].ActiveIncidents.Should()
            .BeEmpty("dispatcher must invalidate the page cache so the resolved transition is visible");
        afterResolve.OverallStatus.Should().Be("operational");
    }

    private static async Task<PublicStatusResponse> GetPublicAsync(HttpClient client, string slug)
    {
        var response = await client.GetAsync($"/public/status/{slug}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PublicStatusResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static async Task<(Guid OrgId, Guid ServiceId)> SeedOrgAndServiceAsync(ApiFactory factory)
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
        return (org.Id, svc.Id);
    }

    private static async Task<Guid> SeedActiveIncidentAsync(
        ApiFactory factory, Guid serviceId, string title, IncidentSeverity severity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var incident = new Incident(serviceId, title, severity);
        incident.TransitionTo(IncidentStatus.Investigating);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        return incident.Id;
    }

    private static async Task SeedResolvedIncidentAsync(
        ApiFactory factory, Guid serviceId, string title, IncidentSeverity severity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var incident = new Incident(serviceId, title, severity);
        incident.TransitionTo(IncidentStatus.Resolved);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
    }
}
