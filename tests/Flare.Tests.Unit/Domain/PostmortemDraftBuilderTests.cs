using System.Text.Json.Nodes;
using Flare.Core.Entities;
using Flare.Core.Services;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Domain;

public sealed class PostmortemDraftBuilderTests
{
    private static readonly PostmortemDraftBuilder Builder = new();

    private static Incident MakeIncident(IncidentSeverity sev = IncidentSeverity.Sev2) =>
        new(Guid.NewGuid(), "Payment API down", sev);

    private static IncidentEvent EventAt(
        Guid incidentId,
        IncidentEventType type,
        string payload,
        DateTime at,
        Guid? actorId = null)
    {
        var e = new IncidentEvent(incidentId, type, payload, actorId);
        typeof(IncidentEvent).GetProperty(nameof(IncidentEvent.CreatedAt))!
            .SetValue(e, at);
        return e;
    }

    [Fact]
    public void Build_WithCreatedAndResolvedEvents_PopulatesImpactSection()
    {
        var inc = MakeIncident(IncidentSeverity.Sev1);
        var start = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"Payment API down"}""", start),
            EventAt(inc.Id, IncidentEventType.StatusChanged, """{"to":"Resolved"}""", start.AddHours(2)),
        };

        var draft = Builder.Build(inc, events);

        draft.Impact.Should().Contain("Payment API down");
        draft.Impact.Should().Contain("Sev1");
        draft.Impact.Should().Contain("Duration: 02:00:00");
    }

    [Theory]
    [InlineData("""{"to":"Resolved"}""")]   // wire default
    [InlineData("""{"To":"Resolved"}""")]   // System.Text.Json default — PascalCase
    public void Build_StatusChangedPayload_IsCaseInsensitive(string payload)
    {
        var inc = MakeIncident();
        var start = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"x"}""", start),
            EventAt(inc.Id, IncidentEventType.StatusChanged, payload, start.AddMinutes(30)),
        };

        var draft = Builder.Build(inc, events);

        draft.Impact.Should().Contain("Duration: 00:30:00");
        draft.Timeline.Should().Contain("\"Summary\":\"Status: Resolved\"");
    }

    [Fact]
    public void Build_PrefersIncidentResolvedAtOverEventScan()
    {
        var inc = MakeIncident();
        var start = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        typeof(Incident).GetProperty(nameof(Incident.CreatedAt))!.SetValue(inc, start);
        typeof(Incident).GetProperty(nameof(Incident.ResolvedAt))!.SetValue(inc, start.AddMinutes(45));

        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"x"}""", start),
        };

        var draft = Builder.Build(inc, events);

        draft.Impact.Should().Contain("Duration: 00:45:00");
    }

    [Fact]
    public void Build_WithNoResolvedSignal_ReportsOngoing()
    {
        var inc = MakeIncident();
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"x"}""",
                new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        draft.Impact.Should().Contain("Duration: ongoing");
    }

    [Fact]
    public void Build_WithLongRunningIncident_FormatsHoursBeyond99()
    {
        var inc = MakeIncident();
        var start = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
        typeof(Incident).GetProperty(nameof(Incident.CreatedAt))!.SetValue(inc, start);
        typeof(Incident).GetProperty(nameof(Incident.ResolvedAt))!.SetValue(inc, start.AddHours(150));

        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"x"}""", start),
        };

        var draft = Builder.Build(inc, events);

        draft.Impact.Should().Contain("Duration: 150:00:00");
    }

    [Fact]
    public void Build_WithServiceName_IncludesNameInImpact()
    {
        var inc = MakeIncident();
        var draft = Builder.Build(inc, Array.Empty<IncidentEvent>(), serviceName: "Payment API");

        draft.Impact.Should().Contain("Service: Payment API");
        draft.Impact.Should().NotContain(inc.ServiceId.ToString());
    }

    [Fact]
    public void Build_WithoutServiceName_FallsBackToServiceId()
    {
        var inc = MakeIncident();
        var draft = Builder.Build(inc, Array.Empty<IncidentEvent>());

        draft.Impact.Should().Contain($"Service: {inc.ServiceId}");
    }

    [Fact]
    public void Build_TimestampsAllEventsInTimeline()
    {
        var inc = MakeIncident();
        var start = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"x"}""", start),
            EventAt(inc.Id, IncidentEventType.StatusChanged, """{"to":"Investigating"}""", start.AddMinutes(5)),
        };

        var draft = Builder.Build(inc, events);

        var timeline = JsonNode.Parse(draft.Timeline)!.AsArray();
        timeline.Should().HaveCount(2);
        timeline[0]!["At"]!.GetValue<DateTime>().Should().Be(start);
        timeline[1]!["At"]!.GetValue<DateTime>().Should().Be(start.AddMinutes(5));
    }

    [Theory]
    [InlineData(IncidentEventType.SeverityChanged, """{"to":"Sev1"}""", "Severity: Sev1")]
    [InlineData(IncidentEventType.RoleAssigned, """{"role":"Commander","userId":"deadbeef"}""", "Role: Commander")]
    [InlineData(IncidentEventType.CommentAdded, """{"comment":"db latency"}""", "db latency")]
    [InlineData(IncidentEventType.NotificationDispatched, """{"channel":"slack"}""", "Notification dispatched")]
    [InlineData(IncidentEventType.WebhookReceived, """{"source":"grafana"}""", "Webhook received")]
    public void Build_SummarizesEachEventType(IncidentEventType type, string payload, string expectedSummary)
    {
        var inc = MakeIncident();
        var events = new[]
        {
            EventAt(inc.Id, type, payload, new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        draft.Timeline.Should().Contain(expectedSummary);
    }

    [Theory]
    [InlineData(199, 199)]   // below cap — full content preserved
    [InlineData(200, 200)]   // at boundary — full content preserved
    [InlineData(250, 200)]   // above cap — truncated to first 200
    public void Build_CommentSummary_RespectsTruncationBoundary(int inputLength, int expectedLength)
    {
        var inc = MakeIncident();
        var comment = new string('a', inputLength);
        var payload = $$"""{"comment":"{{comment}}"}""";
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.CommentAdded, payload,
                new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        var summary = JsonNode.Parse(draft.Timeline)!.AsArray()[0]!["Summary"]!.GetValue<string>();
        summary.Should().Be(new string('a', expectedLength));
    }

    [Theory]
    [InlineData("""{"to":123}""")]
    [InlineData("""{"to":null}""")]
    [InlineData("""{"to":true}""")]
    [InlineData("""[1,2,3]""")]
    public void Build_NonStringOrNonObjectPayload_FallsBackToUnknown(string payload)
    {
        var inc = MakeIncident();
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.StatusChanged, payload,
                new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        draft.Timeline.Should().Contain("Status: unknown");
    }

    [Fact]
    public void Build_WithMalformedPayload_FallsBackToGenericSummary()
    {
        var inc = MakeIncident();
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.StatusChanged, "not-a-json",
                new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        draft.Timeline.Should().Contain("Status: unknown");
    }

    [Fact]
    public void Build_WithExcessiveEventCount_CapsAndAnnotatesTruncation()
    {
        var inc = MakeIncident();
        var start = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        var events = Enumerable.Range(0, 5005)
            .Select(i => EventAt(inc.Id, IncidentEventType.CommentAdded,
                $$"""{"comment":"event {{i}}"}""",
                start.AddSeconds(i)))
            .ToArray();

        var draft = Builder.Build(inc, events);

        var timeline = JsonNode.Parse(draft.Timeline)!.AsArray();
        timeline.Should().HaveCount(5000);
        draft.Impact.Should().Contain("5 earlier events omitted");
    }

    [Fact]
    public void Build_WithEmptyEvents_ReturnsEmptyDraftWithPlaceholders()
    {
        var inc = MakeIncident();

        var draft = Builder.Build(inc, Array.Empty<IncidentEvent>());

        draft.Impact.Should().Contain("Payment API down");
        draft.Timeline.Should().Be("[]");
        draft.RootCause.Should().BeEmpty();
    }

    [Fact]
    public void Build_PreservesChronologicalOrder()
    {
        var inc = MakeIncident();
        var t1 = new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 14, 9, 5, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 5, 14, 9, 10, 0, DateTimeKind.Utc);

        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.CommentAdded, """{"comment":"third"}""", t3),
            EventAt(inc.Id, IncidentEventType.Created, """{"title":"first"}""", t1),
            EventAt(inc.Id, IncidentEventType.StatusChanged, """{"to":"Investigating"}""", t2),
        };

        var draft = Builder.Build(inc, events);

        var timeline = JsonNode.Parse(draft.Timeline)!.AsArray();
        timeline.Should().HaveCount(3);
        timeline.Select(n => n!["Type"]!.GetValue<string>())
            .Should().Equal("Created", "StatusChanged", "CommentAdded");
    }

}
