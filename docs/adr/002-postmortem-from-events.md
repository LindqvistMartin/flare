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
  new `Postmortem` row in `Draft` status (returning 201 Created) or regenerates
  the existing draft (returning 200 OK).
- `Impact` is computed from the incident header (service name when available,
  falling back to `ServiceId`; severity; title) and the duration between the
  first `Created` event and the resolution time. Resolution time prefers
  `Incident.ResolvedAt` (the aggregate invariant), with the most recent
  `StatusChanged → Resolved` event as a fallback when the timestamp has not been
  propagated yet.
- `Timeline` is a JSON array of `{ At, Type, ActorId, Summary }` records, sorted
  by `CreatedAt` ascending, with `Summary` derived per event type from
  `Payload`. Payload property lookup is case-insensitive so that producers using
  different JSON casings interoperate. Timeline is capped at 5000 entries; an
  excess is reported in `Impact` rather than silently truncated.
- `RootCause` is left empty by design — it is the one section that requires
  human judgment and cannot be inferred from events.

Authors edit `Impact` and `RootCause` through a separate endpoint (planned). The
timeline can be regenerated at any point while the postmortem is in `Draft` —
`Regenerate` rewrites all three derived fields and rejects the call once
`Status == Published`.

Once published, the postmortem is immutable. No regeneration; `Update` is gated
the same way.

## Consequences

**Plus**

- Single source of truth: the timeline a reader sees is the timeline that
  happened, by construction.
- Backfills are free: any incident with events can produce a draft retroactively
  at any time, with no data migration.
- Removing rich-text-editor scope from the timeline reduces frontend work — the
  editor only needs to support `Impact` and `RootCause`.

**Minus**

- `Timeline` duplicates data already in `incident_events`. Accepted: it freezes
  the post-publish view and keeps reads cheap without joining the event table.
- Bespoke summarisation per event type lives in `PostmortemDraftBuilder`. If a
  new event type is added without updating the builder, its entries fall back
  to a generic label. Treated as a contract checked by unit tests.
- Truncation at 5000 events means very long incidents lose the tail of the
  timeline from the materialised view. The events themselves remain in
  `"IncidentEvents"`. If this becomes load-bearing, the read endpoint can
  switch to streaming from `incident_events` directly.

## Concurrency

`Postmortem` is mapped with an optimistic concurrency token (`xmin`). Two
concurrent regenerations against the same `Draft` row produce a
`DbUpdateConcurrencyException`; the endpoint surfaces this as `409 Conflict`.
A race on the first insert is caught via Postgres' unique-key violation
(`SqlState 23505`) and also returns `409`.
