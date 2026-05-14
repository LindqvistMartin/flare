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
        draft.Impact.Should().Contain("02:00:00");
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
        timeline[0]!["At"].Should().NotBeNull();
        timeline[1]!["At"].Should().NotBeNull();
    }

    [Fact]
    public void Build_WithCommentAddedEvents_IncludesCommentsInTimeline()
    {
        var inc = MakeIncident();
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.CommentAdded,
                """{"comment":"investigating db latency"}""",
                new DateTime(2026, 5, 14, 9, 0, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        draft.Timeline.Should().Contain("investigating db latency");
    }

    [Fact]
    public void Build_WithRoleAssignedEvents_IncludesRolesInTimeline()
    {
        var inc = MakeIncident();
        var userId = Guid.NewGuid();
        var events = new[]
        {
            EventAt(inc.Id, IncidentEventType.RoleAssigned,
                $$"""{"role":"Commander","userId":"{{userId}}"}""",
                new DateTime(2026, 5, 14, 9, 5, 0, DateTimeKind.Utc)),
        };

        var draft = Builder.Build(inc, events);

        draft.Timeline.Should().Contain("Commander");
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
        var times = timeline.Select(n => n!["At"]!.GetValue<DateTime>()).ToArray();
        times.Should().BeInAscendingOrder();
    }
}
