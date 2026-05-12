using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AbcClassificationRunConfiguration : IEntityTypeConfiguration<AbcClassificationRun>
{
    public void Configure(EntityTypeBuilder<AbcClassificationRun> builder)
    {
        builder.Property(e => e.ClassAThresholdPercent).HasPrecision(5, 2);
        builder.Property(e => e.ClassBThresholdPercent).HasPrecision(5, 2);
        builder.Property(e => e.TotalAnnualUsageValue).HasPrecision(18, 4);

        builder.HasMany(e => e.Classifications)
            .WithOne(c => c.Run)
            .HasForeignKey(c => c.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
