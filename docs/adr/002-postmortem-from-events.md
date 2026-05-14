# ADR-002: Postmortem materialised from event stream

## Status

Accepted.

## Context

Most postmortem tooling treats the document and the timeline as separate
artifacts. The author copies events from a chat log, a paging system, and a
ticket tracker into a free-text doc. By the time the postmortem is reviewed, the
timeline is a transcription — incomplete by accident, edited for narrative on
purpose, and impossible to reconcile with the live event stream after the fact.

Flare already keeps an authoritative, append-only event stream per incident
(see ADR-001). Asking the author to re-type it into a separate document throws
away the property that makes the events trustworthy.

## Decision

`Postmortem.Timeline` is derived from `IncidentEvent` by `PostmortemDraftBuilder`.

The flow is:

- `POST /api/v1/incidents/{id}/postmortem/generate` loads every event for the
  incident, hands them to `PostmortemDraftBuilder.Build`, and either inserts a
  new `Postmortem` row in `Draft` status or regenerates the existing draft.
- `Impact` is computed from the incident header (service, severity, title) and
  the duration between the `Created` event and the first `StatusChanged → Resolved`.
- `Timeline` is a JSON array of `{ At, Type, ActorId, Summary }` records, sorted
  by `CreatedAt` ascending, with `Summary` derived per event type from `Payload`.
- `RootCause` is left empty by design — it is the one section that requires
  human judgment and cannot be inferred from events.

Authors edit `Impact` and `RootCause` freely. The timeline can be regenerated
at any point while the postmortem is in `Draft` — `Regenerate` rewrites all
three derived fields and is gated by a `Status == Published` check.

Once published, the postmortem is immutable. No regeneration, no edits.

## Consequences

**Plus**

- Single source of truth: the timeline a reader sees is the timeline that
  happened, by construction.
- Backfills are free: any incident with events can produce a draft retroactively
  at any time, with no data migration.
- Removing rich-text-editor scope from the timeline reduces frontend work — the
  editor (planned) only needs to support `Impact` and `RootCause`.

**Minus**

- `Timeline` duplicates data already in `incident_events`. Accepted: it freezes
  the post-publish view and keeps reads cheap without joining the event table.
- Bespoke summarisation per event type lives in `PostmortemDraftBuilder`. If a
  new event type is added without updating the builder, its entries fall back
  to a generic label. Treated as a contract checked by unit tests.
- Very long incidents (>1000 events) materialise into one JSON column. If this
  becomes a bottleneck, the read endpoint will switch to streaming the timeline
  from `incident_events` directly. Not in MVP scope.
