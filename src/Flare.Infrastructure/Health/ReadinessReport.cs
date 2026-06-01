namespace Flare.Infrastructure.Health;

public enum ReadinessStatus
{
    Ready,
    Degraded,
    Unready,
}

// Pure readiness policy. It maps observed dependency state to a status with no I/O and no clock
// of its own — the caller measures the outbox age and passes it in — so every branch, including
// the database-down 503, is unit-testable without a database. The endpoint stays a thin adapter:
// run the probes, hand the results here, map the status to an HTTP result.
public readonly record struct ReadinessReport(
    ReadinessStatus Status,
    bool DatabaseReachable,
    long OutboxLagSeconds,
    bool OutboxDegraded)
{
    public static ReadinessReport Evaluate(
        bool databaseReachable,
        TimeSpan? oldestUnprocessedAge,
        TimeSpan outboxDegradedThreshold)
    {
        // Postgres is the only hard gate: without it every write path fails, so the instance is
        // genuinely unready and must leave the load balancer.
        if (!databaseReachable)
            return new ReadinessReport(ReadinessStatus.Unready, DatabaseReachable: false, OutboxLagSeconds: 0, OutboxDegraded: false);

        // A negative age (clock skew between the writer and this instance) clamps to zero rather
        // than reporting a nonsensical negative lag.
        var lagSeconds = oldestUnprocessedAge is { } age
            ? (long)Math.Max(0, age.TotalSeconds)
            : 0;

        // Outbox backlog is a degraded signal, not a gate. The API still serves, so the instance
        // stays in rotation while monitoring sees the lag. Strict '>' so exactly-at-threshold is ready.
        var degraded = lagSeconds > (long)outboxDegradedThreshold.TotalSeconds;

        return new ReadinessReport(
            degraded ? ReadinessStatus.Degraded : ReadinessStatus.Ready,
            DatabaseReachable: true,
            OutboxLagSeconds: lagSeconds,
            OutboxDegraded: degraded);
    }
}
