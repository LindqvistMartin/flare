using Flare.Core.Entities;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Domain;

public sealed class TitleLengthTests
{
    [Fact]
    public void Incident_TitleAtMaxLength_Accepts()
    {
        var title = new string('A', Incident.MaxTitleLength);
        var act = () => new Incident(Guid.NewGuid(), title, IncidentSeverity.Sev3);
        act.Should().NotThrow();
    }

    [Fact]
    public void Incident_TitleAboveMaxLength_Throws()
    {
        // Without this cap a 10 MB POST body would amplify into 10 MB × broadcast-concurrency
        // JSON allocations per dispatcher tick; sanitizer trims AFTER allocation so the spike
        // happens regardless.
        var title = new string('A', Incident.MaxTitleLength + 1);
        var act = () => new Incident(Guid.NewGuid(), title, IncidentSeverity.Sev3);
        act.Should().Throw<ArgumentException>().WithMessage("*512*");
    }

    [Fact]
    public void Service_NameAtMaxLength_Accepts()
    {
        var name = new string('A', Service.MaxNameLength);
        var act = () => new Service(Guid.NewGuid(), name);
        act.Should().NotThrow();
    }

    [Fact]
    public void Service_NameAboveMaxLength_Throws()
    {
        var name = new string('A', Service.MaxNameLength + 1);
        var act = () => new Service(Guid.NewGuid(), name);
        act.Should().Throw<ArgumentException>().WithMessage("*512*");
    }
}
