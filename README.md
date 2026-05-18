# Flare

**Self-hosted incident management. From alert to postmortem — without the SaaS bill.**

[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com)
[![React](https://img.shields.io/badge/React-18-61dafb.svg)](https://react.dev)
[![CI](https://github.com/LindqvistMartin/flare/actions/workflows/ci.yml/badge.svg)](https://github.com/LindqvistMartin/flare/actions)
[![Tests](https://img.shields.io/badge/tests-97%20passing-brightgreen.svg)](#)

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
   PulseWatch,  │                                                  │
   generic)     │   IAlertIngestionAdapter (4 implementations)     │
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
                │   mttr_by_service_30d, mtta_by_service_30d       │
                │     (materialized views, refreshed every 5 min)  │
                └──────────────────────────────────────────────────┘
                          │                          │
                          ▼                          ▼
              ┌────────────────────────┐  ┌───────────────────────────┐
              │ NotificationDispatcher │  │ MetricsAggregator         │
              │   outbox SKIP LOCKED   │  │   REFRESH CONCURRENTLY 5m │
              └────────────────────────┘  └───────────────────────────┘
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
matview SQL stays fast and independent of event payload format.

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
  SignalR groups and to configured Slack / Teams webhooks. At-least-once on DB,
  at-most-once on the wire — see ADR-003 for the rationale.
- **Slack & Teams channels** — pluggable via `INotificationChannel`; configured
  through `Notifications:Slack:WebhookUrl` and `Notifications:Teams:WebhookUrl`.
  Empty URL = silent skip, so Flare boots without any chat integration.
- **Action item reminders** — `ActionItemReminderService` runs daily, emitting an
  `ActionItemOverdue` outbox row per overdue action item; the dispatcher fans
  these out to chat channels.
- **Realtime UI plumbing** — SignalR hub at `/hubs/flare` with `dashboard` and
  `incident:{id}` groups.
- **Idempotent POSTs** — `Idempotency-Key` header deduplicates write requests for
  five minutes via in-memory cache.
- **Observability** — Serilog JSON logs, OpenTelemetry traces and metrics over
  OTLP, `/metrics` for Prometheus scrape.
