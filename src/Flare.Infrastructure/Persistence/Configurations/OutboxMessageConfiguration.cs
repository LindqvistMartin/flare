using Flare.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        // Partial index: only unprocessed rows are indexed — avoids full-table scan as the
        // append-only table grows. The relay's WHERE ProcessedAt IS NULL filter uses this.
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_OutboxMessages_Unprocessed")
            .HasFilter("""("ProcessedAt" IS NULL)""");

        // Composite index used by both ActionItemReminderService.ComputeNextDelayAsync
        // (latest heartbeat lookup) and OutboxJanitorService.SweepAsync (latest heartbeat
        // preservation + processed-and-old delete plan). Without it, both queries become
        // sequential scans once the table grows past ~100K rows.
        builder.HasIndex(x => new { x.Type, x.CreatedAt })
            .HasDatabaseName("IX_OutboxMessages_Type_CreatedAt");
    }
}
