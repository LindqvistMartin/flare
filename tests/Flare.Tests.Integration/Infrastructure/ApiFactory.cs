using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Notifications;
using Flare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flare.Tests.Integration.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("flare_test")
        .Build();

    private readonly Dictionary<string, string?> _configOverrides = new();
    private bool _registerDispatcherAsSingleton;
    private bool _registerReminderAsSingleton;
    private HttpMessageHandler? _slackHandler;
    private HttpMessageHandler? _teamsHandler;
    private bool _disposed;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        try
        {
            // Force lazy host build, then run migrations explicitly — relying on Program.cs'
            // top-level MigrateAsync via the WebApplicationFactory codepath is racy because the
            // factory short-circuits app.Run().
            _ = Server;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
            await db.Database.MigrateAsync();
        }
        catch
        {
            // Host build or migration failure must not leak the container — without this
            // guard a bad config override or migration regression strands a Postgres
            // container per failed test run in CI.
            await _pg.DisposeAsync();
            throw;
        }
    }

    // Fluent opt-ins: chain before any property that triggers host build (Server, CreateClient,
    // Services). Calling these after host build is a no-op since ConfigureWebHost has already run.

    public ApiFactory WithNotificationOverride(string key, string value)
    {
        _configOverrides[$"Notifications:{key}"] = value;
        return this;
    }

    public ApiFactory WithDispatcherForManualTick()
    {
        // Re-registers NotificationDispatcher as a plain singleton — the BackgroundService is
        // stripped (so no background tick races the assertion) and the test drives one
        // deterministic ProcessOnceAsync call.
        _registerDispatcherAsSingleton = true;
        return this;
    }

    public ApiFactory WithReminderForManualTick()
    {
        // Same pattern as WithDispatcherForManualTick — used by reminder schedule tests
        // that need to call internal ComputeNextDelayAsync / TickAsync deterministically.
        _registerReminderAsSingleton = true;
        return this;
    }

    public ApiFactory WithSlackHandler(HttpMessageHandler handler)
    {
        _slackHandler = handler;
        return this;
    }

    public ApiFactory WithTeamsHandler(HttpMessageHandler handler)
    {
        _teamsHandler = handler;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ConfigureAppConfiguration appends to the IConfiguration chain *after* the
        // appsettings.Local.json source that Program.cs registers — last source wins,
        // so this override beats any developer-local connection string baked into the
        // SUT directory.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var dict = new Dictionary<string, string?>(_configOverrides)
            {
                ["ConnectionStrings:Postgres"] = _pg.GetConnectionString()
            };
            cfg.AddInMemoryCollection(dict);
        });

        builder.ConfigureTestServices(services =>
        {
            // Hosted services register as (IHostedService, implType). RemoveAll<T> matches on
            // ServiceType, so filter by ImplementationType. Use predicate-based iterate-and-remove
            // so a future double-registration does not crash the fixture with InvalidOperationException.
            RemoveHosted<IngestionWorker>(services);
            RemoveHosted<NotificationDispatcher>(services);
            RemoveHosted<MetricsAggregator>(services);
            RemoveHosted<ActionItemReminderService>(services);
            RemoveHosted<OutboxJanitorService>(services);

            if (_registerDispatcherAsSingleton)
            {
                // Tests resolve via GetRequiredService<NotificationDispatcher>() and call
                // internal ProcessOnceAsync directly — see InternalsVisibleTo in Infrastructure.csproj.
                services.AddSingleton<NotificationDispatcher>();
            }
            if (_registerReminderAsSingleton)
            {
                services.AddSingleton<ActionItemReminderService>();
            }

            // Named-client builder actions accumulate across registrations. Clearing the
            // production builder list before re-registering pins the test handler as the
            // primary one regardless of SDK version or registration order quirks.
            if (_slackHandler is not null)
            {
                services.Configure<HttpClientFactoryOptions>(SlackNotificationChannel.HttpClientName,
                    o => o.HttpMessageHandlerBuilderActions.Clear());
                services.AddHttpClient(SlackNotificationChannel.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _slackHandler);
            }
            if (_teamsHandler is not null)
            {
                services.Configure<HttpClientFactoryOptions>(TeamsNotificationChannel.HttpClientName,
                    o => o.HttpMessageHandlerBuilderActions.Clear());
                services.AddHttpClient(TeamsNotificationChannel.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _teamsHandler);
            }
        });
    }

    public async Task CleanAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        // TRUNCATE skips row-level triggers, so the IncidentEvents append-only trigger
        // does not block test cleanup. CASCADE chases all FK dependents in one statement.
        // Adding a new entity? Append its table here — the list is not driven by EF metadata.
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "ActionItems", "Postmortems", "OutboxMessages", "IncidentEvents", "Incidents", "StatusPages", "Services", "Teams", "Organizations" CASCADE""");
        // Matviews do not follow FK CASCADE; refresh them so metrics tests see an empty state
        // matching the wiped base tables. CONCURRENTLY needs the unique index that the migration provides.
        await db.Database.ExecuteSqlRawAsync("""REFRESH MATERIALIZED VIEW CONCURRENTLY mttr_by_service_30d""");
        await db.Database.ExecuteSqlRawAsync("""REFRESH MATERIALIZED VIEW CONCURRENTLY mtta_by_service_30d""");
    }

    public override async ValueTask DisposeAsync()
    {
        // Re-entrancy guard: xUnit's IAsyncLifetime.DisposeAsync and `await using` both call
        // DisposeAsync, so we land here twice for every test. base.DisposeAsync is not
        // documented as idempotent across .NET versions; skipping on the second pass avoids
        // noisy teardown errors when DbContext pool or telemetry exporters flush.
        if (_disposed) return;
        _disposed = true;
        // Tear down the host first so open Npgsql connections close cleanly before the
        // container goes away. Reversing the order leaks connections and produces noisy
        // teardown errors when DbContext pool or telemetry exporters flush.
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // xUnit 2.x IAsyncLifetime.DisposeAsync returns Task; WebApplicationFactory.DisposeAsync
    // returns ValueTask. Explicit interface impl bridges the signatures so both teardown
    // paths converge on the same logic.
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    private static void RemoveHosted<T>(IServiceCollection services) where T : IHostedService
    {
        // Iterate-and-remove tolerates accidental duplicate registrations; SingleOrDefault
        // would explode the fixture with an opaque InvalidOperationException at host build.
        var matches = services
            .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T))
            .ToList();
        foreach (var d in matches)
            services.Remove(d);
    }
}
