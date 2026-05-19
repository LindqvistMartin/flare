namespace Flare.Infrastructure.Caching;

public interface IStatusPageCache
{
    // Single-flight + version-aware load. Concurrent callers on the same cold slug share one
    // loader execution; if an invalidation fires between version capture and write, the
    // computed value is dropped so the next reader sees the post-invalidation state instead
    // of a 30-second stale entry.
    //
    // The loader is invoked at most twice per call (single-flight body + late-arrival
    // recovery against a stale Lazy). It MUST be idempotent against the same scoped
    // services (ASP.NET passes the same scoped DbContext to every invocation) and free of
    // observable side effects — do not increment counters or mutate captured state inside
    // the loader. Returns null for "slug not found in DB"; null is not cached.
    Task<string?> TryGetOrLoadAsync(
        string slug,
        Func<CancellationToken, Task<StatusPageLoad?>> loader,
        CancellationToken ct);

    void Invalidate(string slug);
    Task InvalidateForServiceAsync(Guid serviceId, CancellationToken ct);
}

public readonly record struct StatusPageLoad(string Json, IReadOnlyList<Guid> ServiceIds);
