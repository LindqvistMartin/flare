using Flare.Api.Endpoints;
using Flare.Api.Middleware;
using Flare.Infrastructure.Hubs;
using Flare.Core.Services;
using Flare.Infrastructure;
using Flare.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
    throw new InvalidOperationException("ConnectionStrings:Postgres is required");

builder.Services.AddInfrastructure();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PostmortemDraftBuilder>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173").AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Flare"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("Flare.*")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("Flare.Notifications")
        .AddPrometheusExporter()
        .AddOtlpExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapGet("/", () => "Flare API");
// Liveness: process is up. Deliberately dependency-free so an orchestrator never restarts a
// healthy process during a transient Postgres blip — that is the readiness probe's job.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// Outbox lag past this many seconds is a degraded signal, not a 503: the dispatcher is behind
// (Slack/Teams outage or a crash loop) but the API can still serve, so the instance must stay in
// the load balancer. 60s is ~120 dispatcher poll intervals — well past any transient burst.
const int OutboxLagDegradedThresholdSeconds = 60;

app.MapGet("/healthz/ready", async (FlareDbContext db, IHubContext<FlareHub> hub, CancellationToken ct) =>
{
    // Readiness gates on the dependencies this instance needs to serve a request. Postgres is the
    // only hard gate — without it every write path fails, so a down database is a 503 that pulls
    // the instance out of rotation.
    if (!await db.Database.CanConnectAsync(ct))
        return Results.Json(
            new { status = "unready", database = "down" },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    // Oldest unprocessed outbox row = how far behind the NotificationDispatcher is. A wedged
    // dispatcher surfaces here long before the table grows unbounded. Reported as degraded (still
    // 200) rather than a 503 so monitoring sees the backlog without the load balancer evicting an
    // instance that is otherwise serving fine. Lag is measured against this instance's clock —
    // fine for the single-instance deploy; a multi-instance fleet would compute it DB-side.
    var oldestUnprocessed = await db.OutboxMessages
        .Where(m => m.ProcessedAt == null)
        .OrderBy(m => m.CreatedAt)
        .Select(m => (DateTime?)m.CreatedAt)
        .FirstOrDefaultAsync(ct);
    var outboxLagSeconds = oldestUnprocessed is { } oldest
        ? (long)Math.Max(0, (DateTime.UtcNow - oldest).TotalSeconds)
        : 0;
    var outboxDegraded = outboxLagSeconds > OutboxLagDegradedThresholdSeconds;

    // Reaching this handler is itself the realtime signal: if SignalR were not registered, the
    // IHubContext<FlareHub> parameter would fail to resolve and the request would 500 before here.
    // So we report the realtime plane as wired — deliberately not connected-client count, which is
    // a client-side concern that must never gate an otherwise-serving instance out of rotation.
    _ = hub;

    return Results.Ok(new
    {
        status = outboxDegraded ? "degraded" : "ready",
        database = "up",
        realtime = "up",
        outbox = new { unprocessedLagSeconds = outboxLagSeconds, degraded = outboxDegraded },
    });
});

app.MapIncidentEndpoints();
app.MapServiceEndpoints();
app.MapActionItemEndpoints();
app.MapWebhookEndpoints();
app.MapPostmortemEndpoints();
app.MapMetricsEndpoints();
app.MapStatusPageEndpoints();
app.MapPublicStatusEndpoints();

app.MapHub<FlareHub>("/hubs/flare");

app.MapOpenApi();
app.MapScalarApiReference();
app.MapPrometheusScrapingEndpoint();

app.Run();

public partial class Program { }
