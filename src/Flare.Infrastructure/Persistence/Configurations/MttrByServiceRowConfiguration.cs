using Flare.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class MttrByServiceRowConfiguration : IEntityTypeConfiguration<MttrByServiceRow>
{
    public void Configure(EntityTypeBuilder<MttrByServiceRow> builder)
    {
        builder.HasNoKey().ToView("mttr_by_service_30d");
    }
}
