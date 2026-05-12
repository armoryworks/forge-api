using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AbcClassificationConfiguration : IEntityTypeConfiguration<AbcClassification>
{
    public void Configure(EntityTypeBuilder<AbcClassification> builder)
    {
        builder.Property(e => e.AnnualUsageValue).HasPrecision(18, 4);
        builder.Property(e => e.AnnualDemandQuantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(18, 4);
        builder.Property(e => e.CumulativePercent).HasPrecision(7, 4);

        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.RunId);
        builder.HasIndex(e => e.Classification);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
