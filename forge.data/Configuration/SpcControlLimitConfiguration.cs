using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SpcControlLimitConfiguration : IEntityTypeConfiguration<SpcControlLimit>
{
    public void Configure(EntityTypeBuilder<SpcControlLimit> builder)
    {
        builder.Property(e => e.XBarUcl).HasPrecision(18, 6);
        builder.Property(e => e.XBarLcl).HasPrecision(18, 6);
        builder.Property(e => e.XBarCenterLine).HasPrecision(18, 6);
        builder.Property(e => e.RangeUcl).HasPrecision(18, 6);
        builder.Property(e => e.RangeLcl).HasPrecision(18, 6);
        builder.Property(e => e.RangeCenterLine).HasPrecision(18, 6);
        builder.Property(e => e.SUcl).HasPrecision(18, 6);
        builder.Property(e => e.SLcl).HasPrecision(18, 6);
        builder.Property(e => e.SCenterLine).HasPrecision(18, 6);
        builder.Property(e => e.Cp).HasPrecision(10, 4);
        builder.Property(e => e.Cpk).HasPrecision(10, 4);
        builder.Property(e => e.Pp).HasPrecision(10, 4);
        builder.Property(e => e.Ppk).HasPrecision(10, 4);
        builder.Property(e => e.ProcessSigma).HasPrecision(18, 6);

        builder.HasIndex(e => e.CharacteristicId);
        builder.HasIndex(e => new { e.CharacteristicId, e.IsActive });

        builder.HasOne(e => e.Characteristic)
            .WithMany(c => c.ControlLimits)
            .HasForeignKey(e => e.CharacteristicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
