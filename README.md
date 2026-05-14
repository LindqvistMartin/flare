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
  (Prometheus,  │   IAlertIngestionAdapter (4 implementations)     │
   Grafana,     │   Channel<IngestionJob>   (bounded, DropWrite)   │
   PulseWatch,  │                                                  │
   generic)     │ API: POST /api/v1/incidents/{id}/postmortem/...  │
                │   PostmortemDraftBuilder  (inline, synchronous)  │
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
                │   postmortems, action_items, outbox_messages     │
                └──────────────────────────────────────────────────┘
                                           │
                                           ▼
                          ┌────────────────────────┐
                          │ NotificationDispatcher │
                          │   outbox SKIP LOCKED   │
                          └────────────────────────┘
```

Incident events are append-only at the Postgres trigger level — see
[ADR-001](docs/adr/001-append-only-incident-events.md). Postmortems materialise
from the event stream rather than being typed by hand — see
[ADR-002](docs/adr/002-postmortem-from-events.md).
