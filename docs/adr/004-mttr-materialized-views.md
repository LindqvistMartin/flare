# ADR-004: MTTR / MTTA via Postgres materialized views

## Status

Accepted.

## Context

The dashboard exposes two service-level reliability metrics over a rolling
30-day window:

- **MTTR** (mean time to recovery) — `Incident.ResolvedAt - Incident.CreatedAt`
  averaged per service, plus the 50th percentile.
- **MTTA** (mean time to acknowledge) — `Incident.AcknowledgedAt - Incident.CreatedAt`
  averaged per service.

The naive path is to compute these in the API on every dashboard hit:

```
SELECT service_id, AVG(...), PERCENTILE_CONT(0.5) ...
FROM "Incidents"
WHERE "CreatedAt" >= now() - interval '30 days'
GROUP BY service_id;
```

That works at 10K incidents and falls over later. The query scans every row in
the window, the percentile spills to disk, and the dashboard is the hottest
read surface — every operator open hits this. Caching the result in the
application layer (5-minute in-memory) only moves the cost; it does not avoid
the underlying scan when the cache expires.

There is also a correctness wrinkle. If the same window is recomputed by the
app for every request, two simultaneous dashboard hits do the same work twice.

## Decision

Aggregation lives in Postgres as two materialized views:

- `mttr_by_service_30d` — `(service_id, avg_mttr_ms, p50_mttr_ms, incident_count)`
- `mtta_by_service_30d` — `(service_id, avg_mtta_ms, p50_mtta_ms, incident_count)`

A `MetricsAggregator : BackgroundService` runs `REFRESH MATERIALIZED VIEW
CONCURRENTLY` every five minutes against both views. The `/api/v1/metrics/*`
endpoints project from the view rows directly via mapped read models
(`MttrByServiceRow`, `MttaByServiceRow`).

Both matviews have a unique index on `service_id`, which Postgres requires for
`REFRESH CONCURRENTLY`. The shape of the index also gives the dashboard
endpoint an O(1) seek per service.

## Consequences (+)

- O(1) dashboard reads regardless of incident table size. The aggregate cost
  is paid once every five minutes by a single background process, not once
  per request by the API.
- `PERCENTILE_CONT` stays a native Postgres operator. No hand-rolled C#
  percentile (which historically goes wrong on ties or empty windows).
- `REFRESH CONCURRENTLY` does not block readers during the refresh — the
  matview keeps serving the previous snapshot until the new snapshot is ready.
- The window definition lives in one place — the matview DDL. Adding a 90-day
  rollup or a per-team aggregate is one more matview, not a rewrite of the
  read path.

## Consequences (–)

- Up to five minutes of staleness on the dashboard. A SEV1 resolved at 12:00
  may show in `/api/v1/metrics/mttr` at 12:04 at the earliest, depending on
  where in the refresh cycle the resolve landed. Acceptable for a 30-day
  rollup; not acceptable for active-incident counts (which are read directly
  off `Incidents` and skip the matview).
- The unique-per-`service_id` index is load-bearing. Dropping it breaks
  `REFRESH CONCURRENTLY` and the refresh falls back to an exclusive lock,
  blocking dashboard reads for the duration. The migration creating the
  matviews keeps the index in the same file as the view to prevent a partial
  rollback.
- Materialized views are not FK targets and do not follow `CASCADE`. The
  integration test fixture's `CleanAsync` therefore truncates the base
  tables and then explicitly refreshes both matviews — without that step,
  metrics tests see ghost rows from a previous run.

## Alternatives considered

- **Inline SQL / C# aggregation per request** — rejected. Linear in incident
  count, recomputed for every dashboard open, no shared cache between API
  workers. Bound to fail past ~100K incidents on a free-tier Postgres.
- **TimescaleDB hypertables + continuous aggregates** — rejected for MVP.
  Native continuous aggregates would be incrementally cheaper at scale and
  remove the unique-index-required-for-`REFRESH CONCURRENTLY` constraint.
  In return they introduce a separate operational surface (Docker image,
  version pinning, backup format, hypertable awareness for any future
  schema change). Matviews are not free either — the refresh job, the
  unique index, and the staleness window all carry their own cost. The
  call comes out matview-side because the matview path is mostly stock
  Postgres knowledge that any reviewer already has. Worth revisiting if/when
  the incident table crosses ~10M rows.
- **Application-layer memoization** (per-instance `MemoryCache` for 5 min)
  — rejected. Multiple API replicas would each pay the aggregation cost; the
  matview is shared across replicas via Postgres.

## Scaling path

When the base table outgrows the five-minute refresh budget:

1. Partition `Incidents` by month. The 30-day window then touches at most
   two partitions; older partitions are not scanned during refresh.
2. Switch refresh strategy from `CONCURRENTLY` on the whole view to
   per-partition incremental refresh (manual SQL, no native EF tooling).
3. Optionally introduce TimescaleDB at that point and replace the matviews
   with continuous aggregates. Continuous aggregates are hypertables, not
   views, so the migration replaces the matview definitions; the
   `mttr_by_service_30d` / `mtta_by_service_30d` *names* are kept as thin
   read views over the aggregate so the read endpoints stay unchanged.

The dashboard contract (`/api/v1/metrics/{mttr,mtta,dashboard}`) is stable
under all three steps — the API shape does not change.

## Verification

- The CI fixture's `CleanAsync` issues `REFRESH MATERIALIZED VIEW
  CONCURRENTLY` for both views after each `TRUNCATE` so per-test state is
  consistent — proves the refresh path stays healthy as the schema and the
  surrounding data graph evolve.
- Matview math is pinned by `MetricsAggregateTests`: seeded incidents with
  known resolve and acknowledge gaps, REFRESH via raw SQL, then
  `GET /api/v1/metrics/mttr`, `/mtta`, and `/dashboard` assert the expected
  aggregates plus the open / overdue dashboard signals. The zero-row case
  (a service with no resolved incidents in the window) is covered as a
  separate fact so LEFT JOIN + COALESCE drift surfaces immediately.
- `MetricsAggregator` itself is exercised by `MetricsAggregatorTests`,
  which drives the internal `RefreshAsync` directly via the
  `WithMetricsAggregatorForManualTick` fixture knob — same `internal`
  exposure pattern as `NotificationDispatcher.ProcessOnceAsync`. The
  5-minute poll loop and graceful-shutdown handling are deliberately
  out of scope — those are stock `BackgroundService` behaviour.
