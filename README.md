# Flare

**Self-hosted incident management. From alert to postmortem — without the SaaS bill.**

[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com)
[![React](https://img.shields.io/badge/React-19-61dafb.svg)](https://react.dev)
[![CI](https://github.com/LindqvistMartin/flare/actions/workflows/ci.yml/badge.svg)](https://github.com/LindqvistMartin/flare/actions)
[![Tests](https://img.shields.io/badge/tests-279%20passing-brightgreen.svg)](#)

## Why

FireHydrant lists at $44/user/month. Rootly is $20+/user/month with opaque
Scale-tier pricing. Incident.io is $19-50/user/month with status pages and
on-call gated to higher tiers. PagerDuty starts at $21/user/month.
**Opsgenie, the historical affordable option, shuts down in April 2027 —
~800K users are looking for a replacement right now.**

There is no serious open-source alternative. Dispatch (Netflix) needs Keycloak,
PagerDuty, and Slack just to boot. Nothing else clears 2K stars. The space is
100% SaaS-owned.

Flare fills the gap: full incident lifecycle — webhook ingest, append-only
timeline, auto-drafted postmortems from the event stream, MTTR / MTTA
materialised views, Slack and Teams notifications via outbox, public status
pages — with one `docker compose up`. Self-hosted, single deployable monolith,
.NET 10 on the back, React 19 on the front, Postgres for everything.

## Screenshots

![Dashboard with five active incidents, MTTR 30-day trend, and overdue action items](docs/screenshots/dashboard.png)
![Incident detail with assigned roles, timeline feed, and per-service runbook sidebar](docs/screenshots/incident-detail.png)
![Postmortem auto-drafted from the incident event stream with action items](docs/screenshots/postmortem.png)
![Action items tracker with All / Open / In progress / Done / Overdue filters](docs/screenshots/action-items.png)
![Service catalogue with 30-day incident counts and runbook-ready indicator](docs/screenshots/services.png)
![Command palette: search, navigation, dark-mode toggle, recent incidents](docs/screenshots/cmd-k.png)
![Public status page at /p/:slug — overall status, per-service health, active incidents](docs/screenshots/public-status.png)

> Screenshots are produced locally by `scripts/seed-demo.ps1`, which seeds 30
> days of incident history and then runs a Playwright capture spec.

## Architecture

Single deployable monolith. ASP.NET Core 10 Minimal API on the front edge, EF Core
with Postgres for storage, `BackgroundService` + `Channel<T>` for ingestion and
outbox dispatch, OpenTelemetry for tracing and metrics, Serilog for structured
logs, Scalar UI on `/scalar` for the OpenAPI document.

```
                ┌──────────────────────────────────────────────────┐
   alerts ───▶  │ API: POST /api/v1/webhooks/ingest/{source}       │
  (Prometheus,  │      POST /api/v1/incidents/{id}/postmortem/...  │
   Grafana,     │      GET  /api/v1/metrics/{mttr,mtta,dashboard}  │
   PulseWatch,  │      GET  /public/status/{slug}  (cached 30s)    │
   generic)     │                                                  │
                │   IAlertIngestionAdapter (4 implementations)     │
                │   Channel<IngestionJob>  (bounded, DropWrite)    │
                │   PostmortemDraftBuilder (inline, synchronous)   │
                └──────────────────────────────────────────────────┘
                                           │
                                           ▼
                ┌──────────────────────────────────────────────────┐
                │ IngestionWorker : BackgroundService              │
                │   one transaction:                               │
                │     Incident + IncidentEvent + OutboxMessage     │
                └──────────────────────────────────────────────────┘
                                           │
                                           ▼
                ┌──────────────────────────────────────────────────┐
                │ Postgres                                         │
                │   incidents, incident_events (append-only),      │
                │   postmortems, action_items, outbox_messages,    │
                │   status_pages, mttr_by_service_30d,             │
                │   mtta_by_service_30d                            │
                │     (materialized views, refreshed every 5 min)  │
                └──────────────────────────────────────────────────┘
                          │                          │
                          ▼                          ▼
              ┌────────────────────────┐  ┌───────────────────────────┐
              │ NotificationDispatcher │  │ MetricsAggregator         │
              │   outbox SKIP LOCKED   │  │   REFRESH CONCURRENTLY 5m │
              │   → status-page cache  │  └───────────────────────────┘
              │     invalidation       │
              └────────────────────────┘
```

Incident events are append-only at the Postgres trigger level — see
[ADR-001](docs/adr/001-append-only-incident-events.md). Postmortems materialise
from the event stream rather than being typed by hand — see
[ADR-002](docs/adr/002-postmortem-from-events.md). Notifications are dispatched
via an outbox + `SKIP LOCKED` polling worker that broadcasts to SignalR groups
best-effort — see [ADR-003](docs/adr/003-outbox-notification-dispatch.md). MTTR
and MTTA are aggregated per service over a rolling 30-day window from the
canonical `Incident.ResolvedAt` and `Incident.AcknowledgedAt` timestamps — the
domain state machine writes those atomically with the matching event, so the
matview SQL stays fast and independent of event payload format. The matview
strategy and its scaling path are documented in
[ADR-004](docs/adr/004-mttr-materialized-views.md).

## Quick start

Backend:

```sh
cp src/Flare.Api/appsettings.Local.example.json src/Flare.Api/appsettings.Local.json
# Edit the connection string inside (or export ConnectionStrings__Postgres),
# then:
dotnet run --project src/Flare.Api
```

`appsettings.Local.json` is gitignored. The example file is the canonical
template; the environment variable form is preferred in containers.

Client:

```sh
cd client
npm install
npm run dev
```

Then open `http://localhost:5173/#/dashboard`. The client expects the API at
`http://localhost:5000` (override with the `VITE_API_URL` environment variable).
The dev server CORS is already wired into `Program.cs`.

## Backend features

- **Webhook ingestion** — Prometheus, Grafana, PulseWatch, and a Generic adapter
  behind one `IAlertIngestionAdapter` interface. Inbound requests enqueue into a
  bounded `Channel<IngestionJob>` (DropWrite) so the endpoint returns 202 Accepted
  even when the worker is behind.
- **Append-only timeline** — `IncidentEvents` is protected by a PostgreSQL
  `BEFORE UPDATE OR DELETE` trigger; row mutation throws regardless of caller, ORM,
  or migration framework.
- **Domain state machine** — `Triggered → Investigating → Identified → Monitoring
  → Resolved → Closed`, validated inside the aggregate. Invalid transitions surface
  as RFC 7807 Problem+JSON 422 responses.
- **Auto-drafted postmortems** — `PostmortemDraftBuilder` materialises Impact,
  Timeline, and Root Cause directly from the event stream on demand. Postmortems
  are immutable once `Published`.
- **MTTR / MTTA materialized views** — `mttr_by_service_30d` and
  `mtta_by_service_30d`, refreshed concurrently every five minutes by
  `MetricsAggregator`.
- **Outbox dispatch** — `NotificationDispatcher` polls with
  `FOR UPDATE SKIP LOCKED`, marks rows processed, commits, *then* broadcasts to
  SignalR groups and to configured Slack / Teams webhooks. Within-batch fan-out
  runs concurrently (cap 10) on per-message DbContext scopes so one slow
  webhook does not wedge sibling messages. At-least-once on DB, at-most-once
  on the wire — see ADR-003.
- **Public status pages** — read-only `GET /public/status/{slug}` returns
  per-service current status and 30-day incident count for an operator-curated
  service list. Responses are cached in-process for 30 seconds; the dispatcher
  invalidates affected pages post-commit on `IncidentCreated` and
  `IncidentStatusChanged` so changes surface within one tick instead of waiting
  out the TTL. Admin CRUD lives under `/api/v1/status-pages`; the public
  endpoint sits outside `/api/v1` so a future auth gate on the admin surface
  does not lock customers out of the status page. Admin CRUD is currently
  unauthenticated — auth lands with the next milestone (see ADR-005).
  See [ADR-005](docs/adr/005-status-page-cache.md) for the cache design.
- **Slack & Teams channels** — pluggable via `INotificationChannel`. Webhook URLs
  are validated against an HTTPS host allowlist (`hooks.slack.com`,
  `*.webhook.office.com`) at startup via `ValidateOnStart` — a misconfigured
  webhook fails the boot loudly instead of silently exfiltrating payloads.
  HTTP, loopback, and private/link-local targets are refused (SSRF guard).
  Empty URL = silent skip.
- **Action item reminders** — `ActionItemReminderService` runs every 24 hours
  since the previous successful run (schedule is persisted as a
  `ReminderHeartbeat` outbox row, so it survives process restart). A failed
  tick backs off 15 minutes before retry to avoid hammering the DB on a
  permanent error.
- **Outbox retention** — `OutboxJanitorService` sweeps every 6 hours, deleting
  processed messages older than 30 days; bounds storage and the historical PII
  window for any compliance audit.
- **Notification metrics & traces** — `flare_notification_channel_sends_total`,
  `flare_outbox_messages_processed_total`, and `flare_dispatcher_dropped_total`
  counters on the `Flare.Notifications` meter; `Flare.NotificationDispatcher`
  ActivitySource spans complete the Jaeger trace from POST /incidents through
  the channel POST.
- **Realtime UI plumbing** — SignalR hub at `/hubs/flare` with `dashboard` and
  `incident:{id}` groups.
- **Idempotent POSTs** — `Idempotency-Key` header deduplicates write requests for
  five minutes via in-memory cache.
- **Observability** — Serilog JSON logs, OpenTelemetry traces and metrics over
  OTLP, `/metrics` for Prometheus scrape.
- **React client with realtime dashboard** — Vite + React 19 frontend in
  `client/`. The dashboard subscribes to the `dashboard` SignalR group; open
  incidents, MTTR trend, and the active-incidents table refresh without polling.
- **Incident detail page** — full timeline feed with per-event icons, status
  transitions via an allowed-only dropdown (optimistic update with rollback on
  422), Commander / Communicator / Responder assignment, comment composer with
  zod validation, and a Markdown runbook sidebar pulled from the linked service.
  The page subscribes to the `incident:{id}` SignalR group so transitions and
  events from other operators land without polling.
- **Postmortem viewer** — Generate draft from event stream, Draft / Published
  status chip, attached action items panel with inline create. Impact and Root
  Cause render alongside the materialised timeline so a published postmortem
  reads end-to-end on a single screen.
- **Action items tracker** — `/action-items` shows All / Open / In Progress /
  Done / Overdue tabs with inline status toggle. Due-date chips flip to alert
  tone when overdue.
- **Services + runbook editor** — service catalog with per-service incident
  history; runbook is a Markdown editor with debounced save.
- **Command palette** — global Ctrl/Cmd + K opens a `cmdk`-powered palette with
  navigation actions and a search index over recent incidents.
- **Public status page frontend** — `/p/:slug` renders the customer-facing
  layout (overall status, per-service rows, active incidents) against the
  cached `/public/status/{slug}` endpoint. Separate axios instance without
  credentials keeps the route auth-free even after admin auth lands.

## Roadmap

Deliberately out of scope for the MVP:

- **OIDC authentication** — Keycloak or Authentik in `docker-compose`, JWT bearer
  on `/api/v1/*`, leaving `/public/*` and `/healthz` open. The admin surface is
  unauthenticated today — token-based access is enough for a self-hosted demo — and
  the public/admin endpoint split already anticipates the gate, so adding it does
  not lock customers out of the status page.
- **On-call scheduling engine** — rotations, overrides, escalation policies, and the
  timezone math they bring. That is a product on its own; Flare notifies Slack and
  Teams and stays focused on the response lifecycle rather than paging.
- **AI-assisted postmortem summarization** — an optional LLM pass over the event
  stream to draft narrative Root Cause and Contributing Factors. The deterministic
  `PostmortemDraftBuilder` stays the source of truth; an AI summary is additive.
- **Opsgenie import** — a CLI that reads an Opsgenie export and recreates the matching
  services and incidents. Opsgenie shuts down in April 2027, so a migration path is
  the most direct on-ramp for its users.
- **Terraform provider** — services, integrations, and status pages as declarative
  resources for teams that manage everything else as code.
