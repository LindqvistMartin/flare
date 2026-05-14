using Flare.Core.Entities;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Domain;

public sealed class PostmortemTests
{
    private static Postmortem MakePublished()
    {
        var pm = new Postmortem(Guid.NewGuid(), "impact", "[]", "root cause");
        pm.Publish();
        return pm;
    }

    [Fact]
    public void Update_OnPublishedPostmortem_Throws()
    {
        var pm = MakePublished();

        var act = () => pm.Update("new impact", "new root cause");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*published*");
    }

    [Fact]
    public void Regenerate_OnPublishedPostmortem_Throws()
    {
        var pm = MakePublished();

        var act = () => pm.Regenerate("new impact", "[]", "new root cause");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*published*");
    }

    [Fact]
    public void Regenerate_OnDraftPostmortem_RewritesAllDerivedFields()
    {
        var pm = new Postmortem(Guid.NewGuid(), "old impact", "[]", "old rc");

        pm.Regenerate("new impact", """[{"x":1}]""", "new rc");

        pm.Impact.Should().Be("new impact");
        pm.Timeline.Should().Be("""[{"x":1}]""");
        pm.RootCause.Should().Be("new rc");
        pm.Status.Should().Be(PostmortemStatus.Draft);
    }
}
