# ADR-005: Status page cache — replica-local, race-aware single-flight

## Status

Accepted.

## Context

`GET /public/status/{slug}` is an unauthenticated, publicly embeddable JSON
endpoint. Two cost lines drive its design:

1. **Read load can spike** — incidents that flip overall status get linked
   from chat, support tickets, and external status aggregators. A naive
   "query DB on every read" implementation is one Reddit link away from a
   denial-of-service against the incident database.

2. **Writes are infrequent but invalidating** — incident creation, status
   transition, and admin PUT/DELETE all need the next read to reflect the
   change. A long TTL hides change; a short TTL gives up the read-scale win.

The trade-off is "cache aggressively, invalidate precisely."

## Decision

The status page cache is process-local, 30-second TTL, with race-aware
single-flight on cold misses and selective invalidation from the
notification dispatcher.

Key mechanics:

- `IMemoryCache` keyed by normalised slug, 30-second absolute expiration.
- Single-flight via `ConcurrentDictionary<string, Lazy<Task>>` so N concurrent
  cold readers share one DB load.
- Per-slug monotonic version stamp; readers capture before load, writers
  check before commit. An invalidation that races a load discards the
  loaded snapshot rather than letting it overwrite a fresh value.
- Per-slug monitor (`ConcurrentDictionary<string, object>`) serialises
  `TrySet` and `Invalidate` on the same slug — closes the window where the
  reverse-index update and the cache write would otherwise interleave.
- Reverse index `serviceId → slugs` so the dispatcher can invalidate only
  the pages that bind a service that just changed, not the entire table.
- Eviction callback registered per entry with a `Guid[]` snapshot of the
  serviceIds it owned, so callback-driven cleanup is O(owned), not
  O(reverse-index).

The cache exposes three verbs: `TryGetOrLoadAsync` (read path),
`Invalidate` (single slug), `InvalidateForServiceAsync` (all pages bound
to a service — used by the dispatcher hook).

## Consequences (+)

- Read fan-out under burst is O(1) DB cost regardless of N readers (single
  flight).
- Write invalidation lands within one dispatcher tick instead of waiting
  the full TTL.
- Sanitisation runs once per load (cached output is already safe), not
  per read.
- The cache is the only layer doing work — the public endpoint handler
  is ~25 lines of orchestration.

## Consequences (–)

- **Replica-local.** Two layers carry per-process state that does not
  cross replicas: this cache and the `Idempotency-Key` request cache.
  With multiple Flare replicas behind a load balancer, an invalidation
  applied by replica A does not reach replica B's in-memory state.
  Replica B continues to serve its stale snapshot for up to the
  30-second TTL. Ceiling is therefore the TTL, not the inter-replica
  gap. Acceptable for MVP. A future deployment story (Redis-backed
  shared cache, sticky sessions, or shorter TTL) is a deliberate
  decision, not a discovered surprise.

- **Transient 404 on PUT-during-read.** If an admin PUT or dispatcher
  Invalidate fires in the narrow window between a reader's version
  capture and its `TrySet`, the loaded snapshot is discarded by the
  version check. The reader falls back to whatever the cache currently
  holds — typically null, which the endpoint maps to 404. The next
  read primes the cache normally. Bounded staleness: at most one stale
  read per race. Retrying the load instead would re-open thundering
  herd on the recovery path.

- **Bounded growth.** `_versions` and the per-slug monitor pool are
  monotonic — they keep entries for every slug ever seen so the version
  check stays correct across DELETE+CREATE cycles on the same slug.
  Bound is distinct slugs ever created; with admin auth gating in a
  future milestone this is bounded by operator intent.

## Alternatives considered

- **`IMemoryCache.GetOrCreateAsync` with `SemaphoreSlim` per slug.**
  Closer to the standard library shape, ~60 lines. Rejected because the
  version stamp is the only mechanism that can drop a loaded snapshot
  after `cache.Set` would already have written it — without it, a slow
  reader writes a snapshot that an invalidation just discarded, and the
  next read serves stale data for the full TTL. The single-flight
  semaphore alone does not give that property.

- **Redis-backed shared cache.** Cross-replica invalidation lands cleanly;
  cost is an operational dependency Flare otherwise avoids. Revisit when
  the deployment story requires more than one replica.

- **Push-through invalidation via SignalR.** Replica B subscribes to A's
  invalidation broadcasts. Possible, but couples cache layer to the
  realtime layer for one feature; the current outbox-dispatcher hook is
  the more conservative coupling.

## Verification

- `StatusPageCacheTests` (unit, Testcontainers-free) exercises the
  single-flight, version race, snapshot eviction, late-arrival recovery,
  and reverse-index isolation invariants.
- `PublicStatusPageTests` (integration) exercises end-to-end behaviour
  including the dispatcher invalidation path via `WithDispatcherForManualTick`.
- `StatusPagesCrudTests` covers the admin surface and the cache invalidation
  on PUT / DELETE.
