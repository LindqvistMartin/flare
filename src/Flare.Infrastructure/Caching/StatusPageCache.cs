using System.Collections.Concurrent;
using Flare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Flare.Infrastructure.Caching;

public sealed class StatusPageCache(IMemoryCache cache, IServiceScopeFactory scopeFactory) : IStatusPageCache
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private const string KeyPrefix = "statuspage:";

    // Five maps cover distinct lifetimes and access patterns; they do not collapse into one.
    //   _reverseIndex   serviceId -> slugs cached for it.   Lifetime: span of cached entries.
    //   _versions       slug -> monotonic invalidation seq. Lifetime: forever (cheap, used to
    //                                                       detect Set-after-Invalidate race).
    //   _inflight       slug -> in-flight loader Lazy.       Lifetime: span of one cold load.
    //   _ownedServices  slug -> serviceIds it currently      Lifetime: span of cached entry,
    //                   binds in the reverse index.          mirrors per-entry callback state
    //                                                       so lazy-expired entries can still
    //                                                       be pruned (no callback fires when
    //                                                       eviction happens without access).
    //   _slugLocks      slug -> monitor used to serialize    Lifetime: forever, bounded by
    //                   TrySet / Invalidate on that slug.    distinct slugs ever seen.
    //
    // Lock order across the file: slug lock OUTER, reverse-index bucket lock INNER. Never
    // reverse. OnEviction (which takes bucket locks) only runs synchronously inside a
    // cache.Set / cache.Remove that was issued under the slug lock, so the same thread holds
    // both in order — no cross-thread cycle possible.
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _reverseIndex = new();
    private readonly ConcurrentDictionary<string, long> _versions = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inflight = new();
    private readonly ConcurrentDictionary<string, Guid[]> _ownedServices = new();
    private readonly ConcurrentDictionary<string, object> _slugLocks = new();

    public async Task<string?> TryGetOrLoadAsync(
        string slug,
        Func<CancellationToken, Task<StatusPageLoad?>> loader,
        CancellationToken ct)
    {
        if (TryGet(slug, out var cached)) return cached;

        var result = await AwaitInflightAsync(slug, loader, ct);

        // Late-arrival recovery: an Invalidate may have fired after the inflight loader's
        // TrySet but before its finally removed the Lazy from _inflight. A reader that
        // grabbed the still-registered Lazy in that window observes a completed-but-now-
        // stale snapshot. If the cache no longer holds the value we just received, force one
        // fresh load that bypasses _inflight (otherwise we may reattach to the same stale
        // Lazy on retry). Bounded to one extra load per affected request — Invalidate is
        // rare relative to reads.
        if (result is not null && !TryGet(slug, out _))
        {
            return await FreshLoadAsync(slug, loader, ct);
        }

        return result;
    }

    private async Task<string?> AwaitInflightAsync(
        string slug,
        Func<CancellationToken, Task<StatusPageLoad?>> loader,
        CancellationToken ct)
    {
        var lazy = _inflight.GetOrAdd(slug, _ => new Lazy<Task<string?>>(async () =>
        {
            try
            {
                // Second TryGet: another concurrent miss may have already filled the cache
                // between our first TryGet and GetOrAdd.
                if (TryGet(slug, out var afterMiss)) return afterMiss;

                var capturedVersion = GetVersion(slug);
                var loaded = await loader(ct);
                if (loaded is null) return null;

                TrySet(slug, loaded.Value.Json, loaded.Value.ServiceIds, capturedVersion);
                return loaded.Value.Json;
            }
            finally
            {
                _inflight.TryRemove(slug, out Lazy<Task<string?>>? _);
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazy.Value;
    }

    private async Task<string?> FreshLoadAsync(
        string slug,
        Func<CancellationToken, Task<StatusPageLoad?>> loader,
        CancellationToken ct)
    {
        // Single attempt: if an Invalidate races the load (version moves between capture and
        // TrySet), fall back to whatever the cache holds. A concurrent writer that landed a
        // newer snapshot serves it; otherwise null surfaces as 404 for one request (bounded
        // staleness — see ADR-005). Retrying instead would re-open thundering herd on this
        // recovery path and amplify sustained-Invalidate cost.
        var capturedVersion = GetVersion(slug);
        var loaded = await loader(ct);
        if (loaded is null) return null;
        if (TrySet(slug, loaded.Value.Json, loaded.Value.ServiceIds, capturedVersion))
            return loaded.Value.Json;
        return TryGet(slug, out var post) ? post : null;
    }

    // Narrow test seams for the late-arrival recovery branch — Plant injects a completed
    // Lazy without going through the loader path; Clear removes the planted entry so a
    // subsequent test on the same cache instance does not pick it up. Mutating verbs instead
    // of an exposed ConcurrentDictionary keeps the production surface a single line.
    internal void PlantInflightForTesting(string slug, Lazy<Task<string?>> lazy) =>
        _inflight[slug] = lazy;

    internal void ClearInflightForTesting(string slug) =>
        _inflight.TryRemove(slug, out _);

    public void Invalidate(string slug)
    {
        lock (GetSlugLock(slug))
        {
            // Bump version FIRST so any concurrent reader that captured an older version
            // (and is now about to TrySet) detects the move once it acquires this lock.
            // cache.Remove fires OnEviction synchronously on this thread; that handler is
            // the single source of truth for _ownedServices + reverse-index cleanup, so
            // there is no redundant Invalidate-side cleanup to do here.
            BumpVersion(slug);
            cache.Remove(KeyPrefix + slug);
        }
    }

    public async Task InvalidateForServiceAsync(Guid serviceId, CancellationToken ct)
    {
        if (_reverseIndex.TryGetValue(serviceId, out var slugs))
        {
            string[] snapshot;
            lock (slugs) snapshot = [.. slugs];
            foreach (var s in snapshot) Invalidate(s);
            return;
        }

        // Cold path: reverse index is empty for this service (post-restart, or no GET has
        // primed the cache yet). One selective query — status_pages is low-cardinality and
        // the jsonb @> operator picks just the affected rows.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        var sidLiteral = "[\"" + serviceId + "\"]";
        var affected = await db.StatusPages
            .FromSqlRaw("""SELECT * FROM "StatusPages" WHERE "ServiceIds" @> {0}::jsonb""", sidLiteral)
            .AsNoTracking()
            .Select(p => p.Slug)
            .ToListAsync(ct);
        foreach (var s in affected) Invalidate(s);
    }

    // Internal API used by tests to verify version semantics deterministically.
    internal long GetVersion(string slug) => _versions.GetValueOrDefault(slug, 0L);

    internal bool TryGet(string slug, out string cachedJson)
    {
        if (cache.TryGetValue(KeyPrefix + slug, out string? hit) && hit is not null)
        {
            cachedJson = hit;
            return true;
        }
        cachedJson = string.Empty;
        return false;
    }

    internal bool TrySet(string slug, string json, IReadOnlyList<Guid> serviceIds, long expectedVersion)
    {
        lock (GetSlugLock(slug))
        {
            if (GetVersion(slug) > expectedVersion) return false;

            // Take a defensive snapshot of the serviceIds we are about to register so the
            // eviction callback can prune exactly the buckets this entry owns (O(serviceIds)
            // instead of O(reverseIndex)) and survives Set→Set replacement of the same key.
            var snapshot = serviceIds.ToArray();
            var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl };
            options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
            {
                EvictionCallback = OnEviction,
                State = snapshot,
            });
            cache.Set(KeyPrefix + slug, json, options);
            _ownedServices[slug] = snapshot;

            foreach (var sid in snapshot)
            {
                _reverseIndex.AddOrUpdate(
                    sid,
                    _ => [slug],
                    (_, set) => { lock (set) { set.Add(slug); } return set; });
            }
            return true;
        }
    }

    private object GetSlugLock(string slug) => _slugLocks.GetOrAdd(slug, _ => new object());

    private void BumpVersion(string slug) =>
        _versions.AddOrUpdate(slug, 1L, (_, v) => v + 1L);

    private void OnEviction(object key, object? value, EvictionReason reason, object? state)
    {
        if (key is not string k || !k.StartsWith(KeyPrefix, StringComparison.Ordinal)) return;
        // Every entry is registered with a Guid[] snapshot in TrySet, so state is always non-
        // null for our keys. Walk only the buckets this entry owned (O(serviceIds)) rather
        // than every key in the reverse index — prevents the bucket-count amplification an
        // attacker rotating ServiceIds could otherwise turn into per-eviction sweeps.
        var owned = (Guid[])state!;
        var slug = k[KeyPrefix.Length..];
        _ownedServices.TryRemove(slug, out _);
        RemoveFromBuckets(slug, owned);
    }

    private void RemoveFromBuckets(string slug, Guid[] owned)
    {
        foreach (var sid in owned)
        {
            if (_reverseIndex.TryGetValue(sid, out var bucket))
            {
                lock (bucket) bucket.Remove(slug);
            }
        }
    }
}
