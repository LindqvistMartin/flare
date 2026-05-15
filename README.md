# Flare

**Self-hosted incident management. From alert to postmortem — without the SaaS bill.**

[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com)
[![React](https://img.shields.io/badge/React-18-61dafb.svg)](https://react.dev)

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
[ADR-002](docs/adr/002-postmortem-from-events.md). MTTR and MTTA are aggregated
per service over a rolling 30-day window from the canonical `Incident.ResolvedAt`
and `Incident.AcknowledgedAt` timestamps — the domain state machine writes those
atomically with the matching event, so the matview SQL stays fast and independent
of event payload format.
