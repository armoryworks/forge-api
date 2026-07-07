using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SalesOrderStageLineConfiguration : IEntityTypeConfiguration<SalesOrderStageLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderStageLine> builder)
    {
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.HasIndex(e => e.SalesOrderStageId);
        builder.HasIndex(e => e.SalesOrderLineId);
    }
}
