using Flare.Api.Endpoints;
using Flare.Api.Middleware;
using Flare.Infrastructure.Hubs;
using Flare.Core.Services;
using Flare.Infrastructure;
using Flare.Infrastructure.Health;
using Flare.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

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
// Deployed frontend origins arrive via config (Cors__AllowedOrigins__0=https://...) so a new
// host is an env change, not a code change. The fallback keeps local dev zero-config.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

// Only wire the OTLP exporter when an endpoint is configured. Left unset (as on Render) it
// defaults to http://localhost:4317 and fails every export silently; compose sets it for Jaeger.
var otlpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Flare"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("Flare.*");
        if (otlpConfigured)
            t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Flare.Notifications")
            .AddPrometheusExporter();
        if (otlpConfigured)
            m.AddOtlpExporter();
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FlareDbContext>();
    await db.Database.MigrateAsync();
}

// Health probes hit /healthz every few seconds; logging each at Information drowns the request
// log on Render. Drop those to Verbose (below the sink) while keeping failures at Error.
app.UseSerilogRequestLogging(opts => opts.GetLevel = (ctx, _, ex) =>
    ex != null ? LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/healthz") ? LogEventLevel.Verbose
    : LogEventLevel.Information);
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
    // Thin adapter over ReadinessReport.Evaluate: run the probes, hand the raw observations to the
    // pure policy, then map the status to HTTP. The decision lives in the evaluator so every
    // branch — including the database-down 503 — is unit-testable without a database.
    var databaseReachable = await db.Database.CanConnectAsync(ct);

    TimeSpan? oldestUnprocessedAge = null;
    if (databaseReachable)
    {
        // Oldest unprocessed outbox row = how far behind the NotificationDispatcher is; a wedged
        // dispatcher surfaces here long before the table grows unbounded. Age is measured against
        // this instance's clock — fine for the single-instance deploy; a multi-instance fleet would
        // compute it DB-side.
        var oldest = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (oldest is { } o)
            oldestUnprocessedAge = DateTime.UtcNow - o;
    }

    var report = ReadinessReport.Evaluate(
        databaseReachable,
        oldestUnprocessedAge,
        TimeSpan.FromSeconds(OutboxLagDegradedThresholdSeconds));

    if (report.Status == ReadinessStatus.Unready)
        return Results.Json(
            new { status = "unready", database = "down" },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    // Reaching this point means IHubContext<FlareHub> resolved from DI — if SignalR were not
    // registered the parameter would fail to bind and the request would 500 before here. So we
    // report the realtime plane as wired, deliberately not connected-client count, which is a
    // client-side concern that must never gate an otherwise-serving instance out of rotation.
    _ = hub;

    return Results.Ok(new
    {
        status = report.Status == ReadinessStatus.Degraded ? "degraded" : "ready",
        database = "up",
        realtime = "up",
        outbox = new { unprocessedLagSeconds = report.OutboxLagSeconds, degraded = report.OutboxDegraded },
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
