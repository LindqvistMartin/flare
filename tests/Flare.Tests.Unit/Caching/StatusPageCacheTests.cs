using Flare.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Tests.Unit.Caching;

public sealed class StatusPageCacheTests
{
    [Fact]
    public async Task TryGetOrLoadAsync_FirstCall_RunsLoaderAndReturnsValue()
    {
        var cache = CreateCache();
        var calls = 0;

        var result = await cache.TryGetOrLoadAsync("acme", _ =>
        {
            calls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("body-1", [Guid.NewGuid()]));
        }, CancellationToken.None);

        result.Should().Be("body-1");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task TryGetOrLoadAsync_SecondCall_HitsCacheWithoutLoader()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<StatusPageLoad?> Loader(CancellationToken _)
        {
            calls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("body-cached", [Guid.NewGuid()]));
        }

        await cache.TryGetOrLoadAsync("acme", Loader, CancellationToken.None);
        var second = await cache.TryGetOrLoadAsync("acme", Loader, CancellationToken.None);

        second.Should().Be("body-cached");
        calls.Should().Be(1, "cache hit must skip the loader entirely");
    }

    [Fact]
    public async Task TryGetOrLoadAsync_ConcurrentMissesOnSameSlug_LoadsOnce()
    {
        // Single-flight: two simultaneous misses on a cold slug must share one loader run.
        // This is the M1 thundering-herd guard — without it, the unauthenticated endpoint is
        // a one-line DB-saturation primitive via parallel requests.
        var cache = CreateCache();
        var calls = 0;

        async Task<StatusPageLoad?> SlowLoader(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(40);
            return new StatusPageLoad("body-shared", [Guid.NewGuid()]);
        }

        var t1 = Task.Run(() => cache.TryGetOrLoadAsync("acme", SlowLoader, CancellationToken.None));
        var t2 = Task.Run(() => cache.TryGetOrLoadAsync("acme", SlowLoader, CancellationToken.None));
        var t3 = Task.Run(() => cache.TryGetOrLoadAsync("acme", SlowLoader, CancellationToken.None));
        var results = await Task.WhenAll(t1, t2, t3);

        results.Should().AllBe("body-shared");
        calls.Should().Be(1, "single-flight must coalesce concurrent misses on the same slug");
    }

    [Fact]
    public async Task TryGetOrLoadAsync_InvalidationDuringLoad_AllReadersGetFreshData()
    {
        // B2 + B3 combined contract: when an Invalidate fires after the loader started but
        // before its TrySet commits, the loaded snapshot is discarded. Every reader observing
        // this slug — including the in-flight reader who originally triggered the load — sees
        // post-invalidation data, not the pre-invalidation snapshot. Without this, the in-
        // flight reader sees stale data for one call AND any late reader awaiting the same
        // Lazy serves stale data until its own TTL.
        var cache = CreateCache();
        var loaderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderCalls = 0;

        Task<StatusPageLoad?> Loader(CancellationToken _)
        {
            var thisCall = Interlocked.Increment(ref loaderCalls);
            if (thisCall == 1)
            {
                return GatedAsync();
            }
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("v2", [Guid.NewGuid()]));

            async Task<StatusPageLoad?> GatedAsync()
            {
                loaderStarted.SetResult();
                await loaderRelease.Task;
                return new StatusPageLoad("v1", [Guid.NewGuid()]);
            }
        }

        var firstLoad = cache.TryGetOrLoadAsync("acme", Loader, CancellationToken.None);

        await loaderStarted.Task;
        cache.Invalidate("acme");
        loaderRelease.SetResult();

        var firstResult = await firstLoad;
        firstResult.Should().Be("v2",
            "Invalidate during load discards the pre-invalidation snapshot; FreshLoad provides v2");
        loaderCalls.Should().Be(2,
            "first call ran the gated loader, the FreshLoad recovery ran the second");
    }

    [Fact]
    public async Task TryGetOrLoadAsync_LoaderReturnsNull_DoesNotCache()
    {
        // Caching 404s lets an enumeration attacker pin memory for free, and delete+recreate
        // with the same slug stays broken until TTL expires. Neither is acceptable on a
        // public unauthenticated endpoint.
        var cache = CreateCache();
        var calls = 0;

        async Task<StatusPageLoad?> NullLoader(CancellationToken _)
        {
            calls++;
            await Task.Yield();
            return null;
        }

        var first = await cache.TryGetOrLoadAsync("missing", NullLoader, CancellationToken.None);
        var second = await cache.TryGetOrLoadAsync("missing", NullLoader, CancellationToken.None);

        first.Should().BeNull();
        second.Should().BeNull();
        calls.Should().Be(2, "404 must not be cached");
    }

    [Fact]
    public async Task TryGetOrLoadAsync_ReplaceServiceIds_OldServiceBucketCleaned()
    {
        // Rotation scenario: a page first bound to {oldService} is re-loaded after Invalidate
        // with {newService}. The old service's reverse-index bucket must no longer point at
        // the slug. Without per-slug serialization of TrySet/Invalidate, an Invalidate
        // racing the cache.Set window left orphan slug references in old buckets.
        var cache = CreateCache();
        var oldService = Guid.NewGuid();
        var newService = Guid.NewGuid();

        await cache.TryGetOrLoadAsync("acme", _ =>
            Task.FromResult<StatusPageLoad?>(new StatusPageLoad("v1", [oldService])),
            CancellationToken.None);

        cache.Invalidate("acme");
        await cache.TryGetOrLoadAsync("acme", _ =>
            Task.FromResult<StatusPageLoad?>(new StatusPageLoad("v2", [newService])),
            CancellationToken.None);

        // Invalidating the old service must NOT touch acme (it is no longer bound to it).
        await cache.InvalidateForServiceAsync(oldService, CancellationToken.None);

        var reloadCalls = 0;
        var result = await cache.TryGetOrLoadAsync("acme", _ =>
        {
            reloadCalls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("v3", [newService]));
        }, CancellationToken.None);

        reloadCalls.Should().Be(0, "after rotating ServiceIds, oldService must have no binding to acme");
        result.Should().Be("v2");
    }

    [Fact]
    public async Task TryGetOrLoadAsync_InvalidateDuringFreshLoad_DropsDiscardedSnapshot()
    {
        // When an Invalidate fires during FreshLoad's loader, the loaded snapshot is rejected
        // by the version check. FreshLoad falls back to the cache state — typically null,
        // which the endpoint maps to a transient 404 on the next request. This is the
        // bounded staleness contract: at most one stale read per race, no retry storm.
        var cache = CreateCache();

        var stale = new Lazy<Task<string?>>(() => Task.FromResult<string?>("inflight-v1"));
        _ = stale.Value;
        cache.PlantInflightForTesting("acme", stale);

        var loaderRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var readTask = cache.TryGetOrLoadAsync("acme", async _ =>
        {
            loaderStarted.SetResult();
            await loaderRelease.Task;
            return new StatusPageLoad("discarded-by-invalidate", [Guid.NewGuid()]);
        }, CancellationToken.None);

        await loaderStarted.Task;
        cache.Invalidate("acme");
        loaderRelease.SetResult();

        var result = await readTask;
        result.Should().BeNull(
            "the loaded snapshot was discarded by the version check; FreshLoad returns cache state, not the rejected value");

        cache.ClearInflightForTesting("acme");
    }

    [Fact]
    public async Task TryGetOrLoadAsync_StaleLazyAfterPostSetInvalidation_RecoversWithFreshLoad()
    {
        // Scenario: an inflight loader completed (Lazy.Task = "stale") but its finally has
        // not yet TryRemoved itself from _inflight. An Invalidate fired in between, so the
        // cache is empty even though the Lazy holds a value. A late reader entering the
        // cache miss path grabs the still-registered Lazy and would otherwise serve stale.
        // The post-await TryGet/FreshLoad recovery in TryGetOrLoadAsync must kick in.
        var cache = CreateCache();

        // Plant a completed Lazy directly into _inflight (no cache entry — simulates the
        // narrow window between completion of the original loader and its finally).
        var stale = new Lazy<Task<string?>>(() => Task.FromResult<string?>("stale-v1"));
        _ = stale.Value;
        cache.PlantInflightForTesting("acme", stale);

        var freshCalls = 0;
        var result = await cache.TryGetOrLoadAsync("acme", _ =>
        {
            freshCalls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("fresh-v2", [Guid.NewGuid()]));
        }, CancellationToken.None);

        freshCalls.Should().Be(1, "stale Lazy held a value but cache was empty — must FreshLoad");
        result.Should().Be("fresh-v2");

        // Defensive cleanup so a subsequent test reusing the same cache instance does not
        // see the planted Lazy.
        cache.ClearInflightForTesting("acme");
    }

    [Fact]
    public async Task Invalidate_KeepsVersionCounterMonotonic_AcrossSlugRecreation()
    {
        // Version counter must stay monotonic across the slug's entire lifetime, including
        // DELETE+CREATE cycles. A reset would let a slow pre-Invalidate writer with a higher
        // captured version silently win against a post-recreate snapshot of 0 and poison
        // the cache with the deleted page's data.
        var cache = CreateCache();
        var serviceId = Guid.NewGuid();

        await cache.TryGetOrLoadAsync("acme", _ =>
            Task.FromResult<StatusPageLoad?>(new StatusPageLoad("first-life", [serviceId])),
            CancellationToken.None);
        cache.Invalidate("acme");

        cache.GetVersion("acme").Should().BeGreaterThan(0,
            "version counter must persist past Invalidate so post-recreate readers detect pre-Invalidate writers");

        var calls = 0;
        var result = await cache.TryGetOrLoadAsync("acme", _ =>
        {
            calls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("second-life", [serviceId]));
        }, CancellationToken.None);

        calls.Should().Be(1);
        result.Should().Be("second-life");
    }

    [Fact]
    public async Task InvalidateForServiceAsync_OnlyInvalidatesSlugsBoundToThatService()
    {
        // Reverse-index sanity: invalidating service A must not touch a page bound to
        // service B. Without the state-snapshot OnEviction callback (M4), eviction walks
        // every bucket and the attacker bucket-count amplification surface opens up.
        var cache = CreateCache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await cache.TryGetOrLoadAsync("acme", _ =>
            Task.FromResult<StatusPageLoad?>(new StatusPageLoad("acme-v1", [a])),
            CancellationToken.None);
        await cache.TryGetOrLoadAsync("brand", _ =>
            Task.FromResult<StatusPageLoad?>(new StatusPageLoad("brand-v1", [b])),
            CancellationToken.None);

        await cache.InvalidateForServiceAsync(a, CancellationToken.None);

        var brandLoaderCalls = 0;
        var brandResult = await cache.TryGetOrLoadAsync("brand", _ =>
        {
            brandLoaderCalls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("brand-reloaded", [b]));
        }, CancellationToken.None);
        brandLoaderCalls.Should().Be(0, "brand is bound to service b, unrelated to invalidation of a");
        brandResult.Should().Be("brand-v1");

        var acmeLoaderCalls = 0;
        var acmeResult = await cache.TryGetOrLoadAsync("acme", _ =>
        {
            acmeLoaderCalls++;
            return Task.FromResult<StatusPageLoad?>(new StatusPageLoad("acme-v2", [a]));
        }, CancellationToken.None);
        acmeLoaderCalls.Should().Be(1, "acme is bound to service a, must reload after invalidation");
        acmeResult.Should().Be("acme-v2");
    }

    private static StatusPageCache CreateCache()
    {
        var memory = new MemoryCache(new MemoryCacheOptions());
        // Cold-path InvalidateForServiceAsync needs a scope factory; these tests never trigger
        // that path (reverse index is populated by Set inside TryGetOrLoadAsync), so an empty
        // service provider is enough.
        var services = new ServiceCollection().BuildServiceProvider();
        return new StatusPageCache(memory, services.GetRequiredService<IServiceScopeFactory>());
    }
}
