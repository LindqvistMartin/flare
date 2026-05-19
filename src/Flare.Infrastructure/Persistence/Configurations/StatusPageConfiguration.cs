using Flare.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class StatusPageConfiguration : IEntityTypeConfiguration<StatusPage>
{
    public void Configure(EntityTypeBuilder<StatusPage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasMaxLength(StatusPage.MaxSlugLength).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(StatusPage.MaxTitleLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(StatusPage.MaxDescriptionLength);
        builder.Property(x => x.ServiceIdsJson)
            .HasColumnName("ServiceIds")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.OrgId);
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrgId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
