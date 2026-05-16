# ADR-003: Outbox-driven notification dispatch

## Status

Accepted.

## Context

Several incident lifecycle events should fan out to subscribers — the SignalR
dashboard, and (later) Slack/Teams webhooks. The naive path is to send the
notification straight from the request handler that wrote the incident change.

That path has two failure modes:

- **Commit-then-send is unsafe.** If the DB transaction commits and the
  notification call then crashes (or the process dies), the change is visible
  in the DB but no one was notified.
- **Send-then-commit is also unsafe.** Notification fires, then the transaction
  rolls back due to a downstream constraint violation, and subscribers act on
  state that never existed.

Both modes are observable to the client and very hard to detect after the fact.

## Decision

Status-changing endpoints write an `OutboxMessage` row in the same EF Core
transaction as the `IncidentEvent` they emit. A `NotificationDispatcher`
`BackgroundService` polls `"OutboxMessages"` every 500ms with
`FOR UPDATE SKIP LOCKED LIMIT 50`, dispatches each message, marks
`ProcessedAt`, and commits.

Today three `Type` values are produced:

- `IncidentCreated` — broadcast to SignalR group `dashboard`
- `IncidentStatusChanged` — broadcast to `dashboard` and `incident:{IncidentId}`
- `IncidentEventAdded` — broadcast to `incident:{IncidentId}`

SignalR is wired through `IHubContext<FlareHub>`, which requires the dispatcher
host project (`Flare.Infrastructure`) to reference `Microsoft.AspNetCore.App`.
That bends the clean-architecture rule "Infrastructure does not know ASP.NET";
the project already references hosting abstractions, so this is an extension of
the same accepted compromise rather than a new one. The alternative — an
`IFlareNotifier` abstraction in Core with the implementation in Api and the
dispatcher relocated — adds a layer with no behavioural difference for MVP.

Broadcast inside the dispatcher is **best-effort**:

- Each `SendAsync` is wrapped in `try/catch`. Transient failures (no
  connections in the group, a flaky hub, a serialization edge case) are
  logged as warnings.
- `MarkProcessed()` runs regardless. Retrying broadcasts would turn a single
  bad payload into a poison message that burns the outbox forever.
- Clients refetch on (re)connect, so a missed broadcast is observable for at
  most one client refresh cycle.

## Consequences

**Plus**

- Transactional consistency on write: the message and the state change either
  both land or neither does.
- At-least-once delivery from the database's perspective. The dispatcher can
  crash and resume — `SKIP LOCKED` makes multiple dispatcher instances safe.
- Slack/Teams adapters in a later change plug into the same `switch (msg.Type)`
  path. The endpoint side does not change.

**Minus**

- Latency floor of ~500ms from the polling interval. Acceptable for the
  use-case; not a real-time bus.
- Best-effort SignalR means a transient drop is invisible to the server.
  Mitigated by client-side refetch; revisit if losses become observable.
- **Ordering is not total.** `ORDER BY "CreatedAt" LIMIT 50` is not a total
  order under concurrent inserts (timestamp ties, clock skew). The dispatcher
  is at-least-once but not in-order. Frontend handlers must be tolerant — each
  payload carries the `IncidentId` and is independently meaningful.
- Test-mode telemetry: OpenTelemetry OTLP exporter will attempt to connect to
  `localhost:4317` from the integration test host. Out of scope here; will
  silence in a later change if the noise becomes load-bearing.
