using Flare.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flare.Infrastructure.Persistence.Configurations;

internal sealed class MttaByServiceRowConfiguration : IEntityTypeConfiguration<MttaByServiceRow>
{
    public void Configure(EntityTypeBuilder<MttaByServiceRow> builder)
    {
        builder.HasNoKey().ToView("mtta_by_service_30d");
    }
}
