using Flare.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.HasIndex(x => new { x.ServiceId, x.Status });
        builder.HasIndex(x => new { x.ServiceId, x.CreatedAt });
        builder.HasOne(x => x.Service)
            .WithMany(x => x.Incidents)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
