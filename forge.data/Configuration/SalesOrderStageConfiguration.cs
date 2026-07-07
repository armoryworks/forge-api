using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SalesOrderStageConfiguration : IEntityTypeConfiguration<SalesOrderStage>
{
    public void Configure(EntityTypeBuilder<SalesOrderStage> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.SalesOrderId);
    }
}
