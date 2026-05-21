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
`SaveChangesAsync` as the `IncidentEvent` they emit. EF Core wraps a single
`SaveChangesAsync` call in an implicit transaction, so all `db.*.Add` calls
between two saves either land together or not at all. **Convention:** producer
sites that need a second `SaveChangesAsync` (e.g. dedup-check pattern) must
wrap their adds in an explicit `BeginTransactionAsync` block. Five producers
follow this convention today: `IngestionWorker`, manual
`POST /api/v1/incidents`, `POST /api/v1/incidents/{id}/transition`,
`POST /api/v1/incidents/{id}/events`, and the daily
`ActionItemReminderService` background tick.

A `NotificationDispatcher` `BackgroundService` polls `"OutboxMessages"` every
500ms with `FOR UPDATE SKIP LOCKED LIMIT 50`:

1. Read up to 50 unprocessed rows.
2. Mark each `ProcessedAt`.
3. `SaveChangesAsync` + `CommitAsync`.
4. **After** the transaction commits, broadcast each message via
   `IHubContext<FlareHub>`.

Five `Type` values are produced (canonical strings in
`Flare.Core.Entities.OutboxMessageTypes`):

- `IncidentCreated` — broadcast to SignalR group `dashboard`; fans out to chat
  channels.
- `IncidentStatusChanged` — broadcast to `dashboard` always, plus
  `incident:{IncidentId}` when the payload contains a parseable IncidentId;
  fans out to chat channels.
- `IncidentEventAdded` — broadcast to `incident:{IncidentId}` only. Chat
  channels are deliberately skipped: every comment, severity change, and role
  assignment would otherwise become a Slack ping.
- `ActionItemOverdue` — emitted by `ActionItemReminderService` once a day per
  overdue item; channels-only (no SignalR surface yet).
- `ReminderHeartbeat` — internal schedule watermark written by
  `ActionItemReminderService` at the end of every successful tick. Dispatcher
  recognizes the type and silently skips broadcast (no SignalR, no channels,
  no DispatcherDropped counter). Read by `ComputeNextDelayAsync` on the next
  iteration so the 24h cadence survives process restarts. Purged by
  `OutboxJanitorService` like any other processed row, except the most recent
  heartbeat is preserved regardless of age.

The `dashboard`-then-`incident:{id}` split for `IncidentStatusChanged` is
deliberate: the dashboard view should never miss a status change because the
payload was malformed, even if the incident-scoped overlay does.

SignalR is wired through `IHubContext<FlareHub>`, which requires the dispatcher
host project (`Flare.Infrastructure`) to reference `Microsoft.AspNetCore.App`.
That bends the clean-architecture rule "Infrastructure does not know ASP.NET";
the project already references hosting abstractions, so this is an extension of
the same accepted compromise rather than a new one. The alternative — an
`IFlareNotifier` abstraction in Core with the implementation in Api and the
dispatcher relocated — adds a layer with no behavioural difference for MVP.

Broadcast is **best-effort, post-commit**:

- Each `SendAsync` is wrapped in `try/catch`. Transient failures (no
  connections in the group, a flaky hub, a serialization edge case) are
  logged as warnings; the row stays marked processed.
- A broadcast failure or a malformed payload **loses** the notification —
  retrying would turn a bad payload into a poison message that burns the outbox
  forever. Clients refetch on (re)connect to recover, so a missed broadcast is
  observable for at most one client refresh cycle.
- The earlier "broadcast inside the transaction" variant produced **duplicate**
  events on commit failure: broadcast fires, commit throws, next tick re-reads
  the same rows and re-broadcasts. Post-commit broadcast eliminates the
  duplicate path at the cost of trading at-least-once for at-most-once on the
  wire (still at-least-once in the DB).

## Consequences

**Plus**

- Transactional consistency on write: the message and the state change either
  both land or neither does.
- No duplicate broadcasts under DB-write failure — clients trust each event.
- Channel fan-out is a separate post-commit step using `INotificationChannel`
  implementations registered as a singleton collection. Adding a new channel
  (PagerDuty, Discord, custom webhook) is one class plus a named-`HttpClient`
  registration. Each channel runs inside `SafeSendAsync` — a single 5xx Slack
  response or hung Teams endpoint does not block sibling channels for the same
  message. Within a batch, messages now fan out concurrently (cap 10) via a
  per-message scoped `DbContext`, so 50 sequential 10-second timeouts under a
  channel outage no longer wedge the dispatcher for eight minutes.

**Webhook trust boundary**

The `WebhookUrl` strings are treated as a trust boundary. A
`NotificationOptionsValidator` (`IValidateOptions<NotificationOptions>` registered
with `ValidateOnStart`) refuses any URL that is not absolute HTTPS, that points
at loopback, private CIDRs (10/8, 172.16/12, 192.168/16), link-local
(169.254/16 — covers the cloud-metadata IMDS endpoint), IPv6 ULA / link-local,
or any host outside the allowlist of supported chat-platform endpoints
(`hooks.slack.com`, `*.webhook.office.com`, `outlook.office.com`,
`outlook.office365.com`). The named-`HttpClient` handler is configured with
`AllowAutoRedirect = false` so an inbound `30x` cannot re-emit the bearer URL
to an attacker host. The webhook URL itself must be treated as a secret — keep
it out of `appsettings.json` / `appsettings.Production.json` and supply via env
var, user-secrets, or KeyVault.

**Log sanitization**

`HttpRequestException` and `SocketException` messages frequently include the
full request URI, which for Slack/Teams incoming webhooks IS the bearer
credential. `SlackNotificationChannel` / `TeamsNotificationChannel` catch the
HTTP transport exceptions inside their `SendAsync` and re-throw
`NotificationChannelException` carrying only the channel name and the original
exception type — no URL, no inner-exception chain. `SafeSendAsync` upstream
logs the wrapped exception, so log aggregators never see the webhook URL.

**Payload sanitization**

`PayloadSanitizer.Sanitize` runs every user-controlled string (`Title`,
`ServiceName`, `Detail`) through CR/LF/U+2028/U+2029 stripping and HTML-entity
escaping for `& < >`. Without this, an attacker title
`<https://evil/|click to ack>` would render inside a SEV1 Slack card as a
clickable phishing link, and an embedded `\n*[SEV1] PROD DOWN*` would forge a
second visual block. Title is also capped at 256 characters defensively so a
megabyte-sized POST `/api/v1/incidents` cannot translate into a megabyte of
JSON buffered through the webhook HttpClient.

**Observability**

The dispatcher emits Activity spans (`Dispatcher.Tick`,
`Dispatcher.Broadcast`, `Channel.Send`) under the `Flare.NotificationDispatcher`
`ActivitySource` — visible in Jaeger when the OTLP exporter is wired. The
`Flare.Notifications` `Meter` exposes three counters via `/metrics`:
`flare_notification_channel_sends_total{channel,result}`,
`flare_outbox_messages_processed_total{type,result}`, and
`flare_dispatcher_dropped_total{reason}`. The last one is the audit signal —
non-zero means a message was mark-processed but no broadcast reached anyone
(unknown type, incident deleted between enqueue and dispatch, etc).

**Retention**

`OutboxJanitorService` deletes processed messages older than 30 days every 6
hours. Bounds storage growth and gives compliance auditors a finite PII
retention window for the `Payload` jsonb (titles, owner IDs, action-item
content).

**Minus**

- Latency floor of ~500ms from the polling interval. Acceptable for the
  use-case; not a real-time bus.
- Channel hydration costs one `SELECT incidents JOIN services` per processed
  message because the outbox payload only carries `IncidentId`. Acceptable at
  MVP throughput (one scoped DbContext per tick, projected via `AsNoTracking`);
  if dispatch ever sees > ~100 msg/s we will inline the Service name and
  severity into the outbox payload to remove the read.
- Best-effort SignalR + post-commit means a transient broadcast drop is
  invisible to the server. Mitigated by client-side refetch; revisit if losses
  become observable.
- **Ordering is not total.** The dominant source of out-of-order delivery is
  `SKIP LOCKED` itself: dispatcher instance A grabs rows 1–50 and commits while
  instance B (or A's next tick after a slow batch) grabs row 51 *before* A
  finishes broadcasting. Timestamp ties and clock skew add a smaller jitter on
  top. The dispatcher is at-least-once-on-DB / at-most-once-on-wire, never
  in-order. Frontend handlers must be tolerant — each payload carries the
  `IncidentId` and is independently meaningful.
- **Channel-down still wedges briefly.** Within-batch concurrency caps at 10,
  so 50 messages × 10-second timeout = ~50 seconds per tick under a full outage
  rather than 500 seconds. Acceptable for MVP; further reduction needs a
  per-channel circuit breaker.

## Roadmap (deferred, not consequences of this decision)

- **No broadcast for `RoleAssigned` yet.** `POST /api/v1/incidents/{id}/roles`
  writes an `IncidentEvent` but no outbox row, so an `incident:{id}` subscriber
  only learns about role changes on the next refetch. The frontend timeline
  that consumes the group is now shipped, so closing this gap (one extra
  `OutboxMessage` write inside the same `SaveChangesAsync` as the
  `IncidentEvent`, dispatcher already routes by `Type`) is a small, isolated
  follow-up rather than a coupled change.
- **No hub auth.** `FlareHub.JoinIncident(Guid)` accepts any caller. Acceptable
  while authentication is deferred (see project concept's scope guards);
  revisit when auth lands.
- **No POST `/api/v1/incidents` rate limit.** An attacker can spam manual POSTs
  without auth, which fans out to Slack and risks rate-limiting the operator
  workspace. Deferred to the auth / rate-limiter milestone — the dispatcher's
  metrics will surface the symptom (`flare_notification_channel_sends_total`
  with `result="error"` rising) before the workspace is banned.
- **429 has no replay.** Slack returns `Retry-After` on rate limits and the
  channel logs it with the message, but the outbox row is already mark-processed
  by the time the channel runs — there is no requeue. A storm of 429s loses
  notifications. Mitigation today is the metrics counter; a real retry table
  is roadmap.
- Test-mode telemetry: OpenTelemetry OTLP exporter will attempt to connect to
  `localhost:4317` from the integration test host. Out of scope here; will
  silence in a later change if the noise becomes load-bearing.
