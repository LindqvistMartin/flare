using Flare.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class IncidentEventConfiguration : IEntityTypeConfiguration<IncidentEvent>
{
    public void Configure(EntityTypeBuilder<IncidentEvent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.IncidentId, x.CreatedAt });
        builder.HasOne(x => x.Incident)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
