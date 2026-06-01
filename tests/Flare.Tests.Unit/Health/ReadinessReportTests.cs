using Flare.Infrastructure.Health;
using FluentAssertions;
using Xunit;

namespace Flare.Tests.Unit.Health;

public sealed class ReadinessReportTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(60);

    [Fact]
    public void Evaluate_DatabaseUnreachable_IsUnreadyAndIgnoresLag()
    {
        // The 503 branch: the hard gate failed, so the instance must leave rotation regardless of
        // any (now unreadable) outbox state. This is the branch an integration test cannot reach
        // without tearing down Postgres mid-run — covered here for free.
        var report = ReadinessReport.Evaluate(databaseReachable: false, oldestUnprocessedAge: TimeSpan.FromHours(1), Threshold);

        report.Status.Should().Be(ReadinessStatus.Unready);
        report.DatabaseReachable.Should().BeFalse();
        report.OutboxLagSeconds.Should().Be(0, "lag is irrelevant once the hard gate has failed");
        report.OutboxDegraded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DatabaseUp_NoBacklog_IsReady()
    {
        var report = ReadinessReport.Evaluate(databaseReachable: true, oldestUnprocessedAge: null, Threshold);

        report.Status.Should().Be(ReadinessStatus.Ready);
        report.DatabaseReachable.Should().BeTrue();
        report.OutboxLagSeconds.Should().Be(0);
        report.OutboxDegraded.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)] // exactly at threshold — strict '>' keeps it ready
    public void Evaluate_LagAtOrBelowThreshold_IsReady(int lagSeconds)
    {
        var report = ReadinessReport.Evaluate(true, TimeSpan.FromSeconds(lagSeconds), Threshold);

        report.Status.Should().Be(ReadinessStatus.Ready);
        report.OutboxDegraded.Should().BeFalse();
        report.OutboxLagSeconds.Should().Be(lagSeconds);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(3600)]
    public void Evaluate_LagAboveThreshold_IsDegradedButStillReachable(int lagSeconds)
    {
        var report = ReadinessReport.Evaluate(true, TimeSpan.FromSeconds(lagSeconds), Threshold);

        report.Status.Should().Be(ReadinessStatus.Degraded);
        report.DatabaseReachable.Should().BeTrue("degraded is a 200 signal, not a gate");
        report.OutboxDegraded.Should().BeTrue();
        report.OutboxLagSeconds.Should().Be(lagSeconds);
    }

    [Fact]
    public void Evaluate_NegativeAge_ClampsLagToZero()
    {
        // Clock skew can make the row's timestamp appear in the future relative to this instance.
        var report = ReadinessReport.Evaluate(true, TimeSpan.FromSeconds(-5), Threshold);

        report.Status.Should().Be(ReadinessStatus.Ready);
        report.OutboxLagSeconds.Should().Be(0);
    }
}
