using Flare.Api.Endpoints;
using Flare.Api.Hubs;
using Flare.Api.Middleware;
using Flare.Core.Services;
using Flare.Infrastructure;
using Flare.Infrastructure.Persistence;
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

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

builder.Services.AddInfrastructure(connectionString);
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
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/healthz/ready", async (FlareDbContext db, CancellationToken ct) =>
{
    var ok = await db.Database.CanConnectAsync(ct);
    return ok ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
});

app.MapIncidentEndpoints();
app.MapServiceEndpoints();
app.MapActionItemEndpoints();
app.MapWebhookEndpoints();
app.MapPostmortemEndpoints();
app.MapMetricsEndpoints();

app.MapHub<FlareHub>("/hubs/flare");

app.MapOpenApi();
app.MapScalarApiReference();
app.MapPrometheusScrapingEndpoint();

app.Run();

public partial class Program { }
