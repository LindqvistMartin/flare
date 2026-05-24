using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Api.Contracts;
using Flare.Core.Entities;
using Flare.Infrastructure.Persistence;
using Flare.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Integration.Endpoints;

// Locks in the 422 + application/problem+json contract for disallowed state
// transitions. The state machine matrix lives in Incident.cs and its unit-test
// counterpart covers the matrix exhaustively; this fixture covers the *wire*
// shape — a CTO clicking around with Postman should see RFC 7807, not a 500.
[Collection("Api")]
public sealed class TransitionFailureTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.CleanAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostDisallowedTransition_ReturnsProblemJsonWith422()
    {
        var serviceId = await SeedAndReturnServiceIdAsync();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/incidents",
            new CreateIncidentRequest("Payment latency spike", "Sev2", serviceId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var incident = await createResponse.Content.ReadFromJsonAsync<IncidentResponse>();
        incident.Should().NotBeNull();

        // Triggered → Closed is not in AllowedTransitions; matrix permits only
        // Investigating / Identified / Resolved from Triggered. Picking Closed
        // gives the cleanest "obviously wrong" wire signal.
        var transitionResponse = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident!.Id}/transition",
            new TransitionRequest("Closed"));

        transitionResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        transitionResponse.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        var body = await transitionResponse.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("title").GetString().Should().Be("Invalid transition");
        problem.GetProperty("status").GetInt32().Should().Be(422);
        // detail surfaces the domain exception message — "Cannot transition from Triggered to Closed.".
        problem.GetProperty("detail").GetString().Should().Contain("Triggered").And.Contain("Closed");
    }

    [Fact]
    public async Task PostUnknownTransitionTarget_Returns400ProblemJson()
    {
        var serviceId = await SeedAndReturnServiceIdAsync();
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/incidents",
            new CreateIncidentRequest("Payment latency spike", "Sev2", serviceId));
        var incident = (await createResponse.Content.ReadFromJsonAsync<IncidentResponse>())!;

        var response = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/transition",
            new TransitionRequest("Sideways"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    private async Task<Guid> SeedAndReturnServiceIdAsync()
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
        return svc.Id;
    }
}
