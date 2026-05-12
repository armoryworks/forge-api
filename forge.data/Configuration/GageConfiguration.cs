using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class GageConfiguration : IEntityTypeConfiguration<Gage>
{
    public void Configure(EntityTypeBuilder<Gage> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.GageNumber).IsUnique();
        builder.HasIndex(e => e.LocationId);
        builder.HasIndex(e => e.AssetId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.NextCalibrationDue);

        builder.Property(e => e.GageNumber).HasMaxLength(50);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.GageType).HasMaxLength(100);
        builder.Property(e => e.Manufacturer).HasMaxLength(200);
        builder.Property(e => e.Model).HasMaxLength(200);
        builder.Property(e => e.SerialNumber).HasMaxLength(100);
        builder.Property(e => e.AccuracySpec).HasMaxLength(100);
        builder.Property(e => e.RangeSpec).HasMaxLength(100);
        builder.Property(e => e.Resolution).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
