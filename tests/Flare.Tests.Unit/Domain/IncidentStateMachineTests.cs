using Flare.Core.Entities;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Domain;

public sealed class IncidentStateMachineTests
{
    private static Incident Make() => new(Guid.NewGuid(), "Test incident", IncidentSeverity.Sev2);

    private static Incident InStatus(IncidentStatus target)
    {
        var inc = Make();
        if (target == IncidentStatus.Triggered) return inc;
        inc.TransitionTo(IncidentStatus.Investigating);
        if (target == IncidentStatus.Investigating) return inc;
        inc.TransitionTo(IncidentStatus.Identified);
        if (target == IncidentStatus.Identified) return inc;
        inc.TransitionTo(IncidentStatus.Monitoring);
        if (target == IncidentStatus.Monitoring) return inc;
        inc.TransitionTo(IncidentStatus.Resolved);
        if (target == IncidentStatus.Resolved) return inc;
        inc.TransitionTo(IncidentStatus.Closed);
        return inc;
    }

    [Theory]
    [InlineData(IncidentStatus.Triggered, IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Triggered, IncidentStatus.Identified)]
    [InlineData(IncidentStatus.Triggered, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Identified)]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Identified, IncidentStatus.Monitoring)]
    [InlineData(IncidentStatus.Identified, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Monitoring, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed)]
    public void TransitionTo_AllowedTransition_DoesNotThrow(IncidentStatus from, IncidentStatus to)
    {
        var incident = InStatus(from);
        var act = () => incident.TransitionTo(to);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Triggered)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Triggered, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Triggered, IncidentStatus.Monitoring)]
    [InlineData(IncidentStatus.Monitoring, IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Identified, IncidentStatus.Triggered)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Resolved)]
    public void TransitionTo_DisallowedTransition_ThrowsInvalidOperationException(IncidentStatus from, IncidentStatus to)
    {
        var incident = InStatus(from);
        var act = () => incident.TransitionTo(to);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Cannot transition from {from} to {to}.");
    }

    [Fact]
    public void TransitionTo_Investigating_SetsAcknowledgedAt()
    {
        var before = DateTime.UtcNow;
        var incident = Make();
        incident.TransitionTo(IncidentStatus.Investigating);
        incident.AcknowledgedAt.Should().NotBeNull();
        incident.AcknowledgedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void TransitionTo_QuickResolveFromTriggered_SetsAcknowledgedAt()
    {
        // Triggered → Resolved skips Investigating; AcknowledgedAt must still be set.
        var before = DateTime.UtcNow;
        var incident = Make();
        incident.TransitionTo(IncidentStatus.Resolved);
        incident.AcknowledgedAt.Should().NotBeNull();
        incident.AcknowledgedAt.Should().BeOnOrAfter(before);
        incident.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void TransitionTo_SkipToIdentified_SetsAcknowledgedAt()
    {
        var before = DateTime.UtcNow;
        var incident = Make();
        incident.TransitionTo(IncidentStatus.Identified);
        incident.AcknowledgedAt.Should().NotBeNull();
        incident.AcknowledgedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void TransitionTo_AcknowledgedAt_SetOnFirstTransitionNotOverwritten()
    {
        var incident = Make();
        incident.TransitionTo(IncidentStatus.Investigating);
        var first = incident.AcknowledgedAt;

        incident.TransitionTo(IncidentStatus.Identified);

        // AcknowledgedAt is guarded with ??= — subsequent transitions must not overwrite it.
        incident.AcknowledgedAt.Should().Be(first);
    }

    [Fact]
    public void TransitionTo_Resolved_SetsResolvedAt()
    {
        var before = DateTime.UtcNow;
        var incident = InStatus(IncidentStatus.Monitoring);
        incident.TransitionTo(IncidentStatus.Resolved);
        incident.ResolvedAt.Should().NotBeNull();
        incident.ResolvedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void TransitionTo_Closed_SetsClosedAt()
    {
        var before = DateTime.UtcNow;
        var incident = InStatus(IncidentStatus.Resolved);
        incident.TransitionTo(IncidentStatus.Closed);
        incident.ClosedAt.Should().NotBeNull();
        incident.ClosedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Constructor_EmptyServiceId_ThrowsArgumentException()
    {
        var act = () => new Incident(Guid.Empty, "Title", IncidentSeverity.Sev1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_BlankTitle_ThrowsArgumentException()
    {
        var act = () => new Incident(Guid.NewGuid(), "  ", IncidentSeverity.Sev1);
        act.Should().Throw<ArgumentException>();
    }
}
