using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorScorecardConfiguration : IEntityTypeConfiguration<VendorScorecard>
{
    public void Configure(EntityTypeBuilder<VendorScorecard> builder)
    {
        builder.Property(e => e.AvgLeadTimeDays).HasPrecision(18, 2);
        builder.Property(e => e.OnTimeDeliveryPercent).HasPrecision(18, 2);
        builder.Property(e => e.QualityAcceptancePercent).HasPrecision(18, 2);
        builder.Property(e => e.TotalSpend).HasPrecision(18, 2);
        builder.Property(e => e.AvgPriceVariancePercent).HasPrecision(18, 2);
        builder.Property(e => e.QuantityAccuracyPercent).HasPrecision(18, 2);
        builder.Property(e => e.OverallScore).HasPrecision(18, 2);

        builder.Property(e => e.CalculationNotes).HasMaxLength(2000);

        builder.Property(e => e.Grade)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.VendorId, e.PeriodStart });
    }
}
