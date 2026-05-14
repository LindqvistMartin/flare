using Flare.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class PostmortemConfiguration : IEntityTypeConfiguration<Postmortem>
{
    public void Configure(EntityTypeBuilder<Postmortem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Impact).HasColumnType("text");
        builder.Property(x => x.Timeline).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.RootCause).HasColumnType("text");
        builder.HasIndex(x => x.IncidentId).IsUnique();
        // Map shadow property to Postgres' system xmin column for optimistic concurrency without a schema change.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
        builder.HasOne(x => x.Incident)
            .WithOne(x => x.Postmortem)
            .HasForeignKey<Postmortem>(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
