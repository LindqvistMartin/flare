using Flare.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flare.Infrastructure.Persistence;

public sealed class FlareDbContext(DbContextOptions<FlareDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentEvent> IncidentEvents => Set<IncidentEvent>();
    public DbSet<Postmortem> Postmortems => Set<Postmortem>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlareDbContext).Assembly);
}
