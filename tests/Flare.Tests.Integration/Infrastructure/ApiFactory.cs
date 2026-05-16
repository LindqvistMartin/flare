using Flare.Infrastructure.BackgroundServices;
using Flare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flare.Tests.Integration.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("flare_test")
        .Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        // Trigger lazy host build — Program.cs runs MigrateAsync on startup.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ConfigureAppConfiguration appends to the IConfiguration chain *after* the
        // appsettings.Local.json source that Program.cs registers — last source wins,
        // so this override beats any developer-local connection string baked into the
        // SUT directory.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _pg.GetConnectionString()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Hosted services register as (IHostedService, implType). RemoveAll<T> matches on
            // ServiceType, so remove by ImplementationType to keep framework infrastructure intact.
            RemoveHosted<IngestionWorker>(services);
            RemoveHosted<NotificationDispatcher>(services);
            RemoveHosted<MetricsAggregator>(services);
        });
    }

    public async Task CleanAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
        // TRUNCATE skips row-level triggers, so the IncidentEvents append-only trigger
        // does not block test cleanup. CASCADE chases all FK dependents in one statement.
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "ActionItems", "Postmortems", "OutboxMessages", "IncidentEvents", "Incidents", "Services", "Teams", "Organizations" CASCADE""");
    }

    public new async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }

    private static void RemoveHosted<T>(IServiceCollection services) where T : IHostedService
    {
        var descriptor = services.SingleOrDefault(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
        if (descriptor is not null)
            services.Remove(descriptor);
    }
}
