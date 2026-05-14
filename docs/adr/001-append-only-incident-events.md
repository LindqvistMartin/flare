# ADR-001: Append-only incident events

## Status

Accepted.

## Context

The postmortem is only as honest as the history it draws from. If past events can
be rewritten or deleted from the application layer, the post-incident review
degrades into "what we remember now" instead of "what actually happened." For an
incident management tool this is the single most important guarantee to make
load-bearing.

EF Core conventions alone do not provide that guarantee. A `private set` on the
entity stops accidental writes from the domain layer, but it is bypassed by raw
SQL, EF interceptors disabled in tests, direct `DbContext` use elsewhere in the
codebase, or any future ORM swap. Mutability remains a property of the database,
not the code.

## Decision

The `"IncidentEvents"` table is append-only enforced at the Postgres trigger level.

The initial migration creates `prevent_incident_event_modification()` and binds it
as a `BEFORE UPDATE OR DELETE` trigger named `trg_incident_events_immutable`. Any
non-INSERT operation raises:

```
incident_events is append-only: modification of existing rows is not permitted
```

regardless of caller, ORM, or connection.

Inserts are unrestricted — the table grows monotonically.

## Consequences

**Plus**

- The timeline is tamper-evident by construction; the audit trail is the data.
- `PostmortemDraftBuilder` (ADR-002) can treat the event stream as a source of
  truth without consistency caveats.
- Compromise of application credentials cannot rewrite incident history without
  separately compromising the database role that owns the trigger.

**Minus**

- Typos and accidents are corrected by appending compensating events, not by
  editing existing ones. The UI will need to make this obvious when an authoring
  surface lands.
- Storage grows monotonically. Partitioning by month is the expected mitigation
  once retention requires it; not in MVP scope.
- Tests that need to seed historical timestamps cannot `UPDATE` after insert and
  must construct events with the timestamps they need. Acceptable.

## Verification

An integration test (planned) attempts `UPDATE` and `DELETE` via raw SQL against
`"IncidentEvents"` and asserts that Postgres raises the trigger exception in both
cases.
