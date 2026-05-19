using System.Net;
using System.Net.Http.Json;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

[Collection("Api")]
public sealed class StatusPagesCrudTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostStatusPage_ThenGetList_ThenGetById_RoundTripsPayload()
    {
        var (orgId, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", "Customer-facing services.",
                [serviceId]));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResponse.Content.ReadFromJsonAsync<StatusPageResponse>())!;
        created.Slug.Should().Be("acme");
        created.Title.Should().Be("Acme Status");
        created.Description.Should().Be("Customer-facing services.");
        created.ServiceIds.Should().ContainSingle().Which.Should().Be(serviceId);
        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        created.UpdatedAt.Should().Be(created.CreatedAt);

        var listResponse = await client.GetAsync("/api/v1/status-pages");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await listResponse.Content.ReadFromJsonAsync<List<StatusPageResponse>>())!;
        list.Should().ContainSingle().Which.Id.Should().Be(created.Id);

        var getResponse = await client.GetAsync($"/api/v1/status-pages/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<StatusPageResponse>();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task PostStatusPage_NormalizesUppercaseSlugToLowercase()
    {
        var (orgId, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "  ACME-Prod  ", "Acme Production", null, [serviceId]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<StatusPageResponse>())!;
        created.Slug.Should().Be("acme-prod");
        created.Description.Should().BeNull();
    }

    [Fact]
    public async Task PostStatusPage_DuplicateSlug_Returns409()
    {
        var (orgId, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Public", null, [serviceId]));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostStatusPage_UnknownServiceId_Returns422()
    {
        var (orgId, _) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [Guid.NewGuid()]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostStatusPage_UnknownOrg_Returns404()
    {
        var (_, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(Guid.NewGuid(), "acme", "Acme Status", null, [serviceId]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac me")]
    [InlineData("acme!")]
    public async Task PostStatusPage_InvalidSlug_Returns422(string slug)
    {
        var (orgId, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, slug, "Acme Status", null, [serviceId]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostStatusPage_EmptyServiceIds_Returns422()
    {
        var (orgId, _) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, Array.Empty<Guid>()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PutStatusPage_UpdatesTitleAndServices_ReturnsUpdatedPayload()
    {
        var (orgId, serviceId) = await SeedOrgAndServiceAsync();
        var (_, otherServiceId) = await AddSecondaryServiceAsync(orgId);
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));
        var created = (await createResponse.Content.ReadFromJsonAsync<StatusPageResponse>())!;

        var putResponse = await client.PutAsJsonAsync($"/api/v1/status-pages/{created.Id}",
            new UpdateStatusPageRequest("Acme Public", "Now multi-service.", [serviceId, otherServiceId]));

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await putResponse.Content.ReadFromJsonAsync<StatusPageResponse>())!;
        updated.Title.Should().Be("Acme Public");
        updated.Description.Should().Be("Now multi-service.");
        updated.ServiceIds.Should().BeEquivalentTo(new[] { serviceId, otherServiceId });
        updated.UpdatedAt.Should().BeOnOrAfter(created.UpdatedAt);
    }

    [Fact]
    public async Task PutStatusPage_FullyReplacesServiceIds_GetByIdReflectsOnlyNewServices()
    {
        // Rotation: page initially bound to {oldService}, replaced via PUT with {newService}.
        // After the PUT, GET-by-id must report only the new service — the old binding is
        // gone, and the dispatcher cache hook will no longer invalidate this page when an
        // incident lands on the old service.
        var (orgId, oldServiceId) = await SeedOrgAndServiceAsync();
        var (_, newServiceId) = await AddSecondaryServiceAsync(orgId);
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [oldServiceId]));
        var created = (await createResponse.Content.ReadFromJsonAsync<StatusPageResponse>())!;

        var putResponse = await client.PutAsJsonAsync($"/api/v1/status-pages/{created.Id}",
            new UpdateStatusPageRequest("Acme Status", null, [newServiceId]));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync($"/api/v1/status-pages/{created.Id}");
        var fetched = (await getResponse.Content.ReadFromJsonAsync<StatusPageResponse>())!;
        fetched.ServiceIds.Should().ContainSingle().Which.Should().Be(newServiceId);
        fetched.ServiceIds.Should().NotContain(oldServiceId);
    }

    [Fact]
    public async Task PutStatusPage_NotFound_Returns404()
    {
        var (_, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/v1/status-pages/{Guid.NewGuid()}",
            new UpdateStatusPageRequest("Renamed", null, [serviceId]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteStatusPage_RemovesRowAndSubsequentGetIs404()
    {
        var (orgId, serviceId) = await SeedOrgAndServiceAsync();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/status-pages",
            new CreateStatusPageRequest(orgId, "acme", "Acme Status", null, [serviceId]));
        var created = (await createResponse.Content.ReadFromJsonAsync<StatusPageResponse>())!;

        var deleteResponse = await client.DeleteAsync($"/api/v1/status-pages/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/status-pages/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(Guid OrgId, Guid ServiceId)> SeedOrgAndServiceAsync()
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

    private async Task<(Guid TeamId, Guid ServiceId)> AddSecondaryServiceAsync(Guid orgId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var team = await db.Teams.FirstAsync(t => t.OrganizationId == orgId);
        var svc = new Service(team.Id, "Auth Service");
        db.Services.Add(svc);
        await db.SaveChangesAsync();
        return (team.Id, svc.Id);
    }
}
