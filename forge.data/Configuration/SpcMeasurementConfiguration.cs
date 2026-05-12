using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SpcMeasurementConfiguration : IEntityTypeConfiguration<SpcMeasurement>
{
    public void Configure(EntityTypeBuilder<SpcMeasurement> builder)
    {
        builder.Property(e => e.ValuesJson).HasColumnType("jsonb");
        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.OocRuleViolated).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Mean).HasPrecision(18, 6);
        builder.Property(e => e.Range).HasPrecision(18, 6);
        builder.Property(e => e.StdDev).HasPrecision(18, 6);
        builder.Property(e => e.Median).HasPrecision(18, 6);

        builder.HasIndex(e => e.CharacteristicId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.ProductionRunId);
        builder.HasIndex(e => e.MeasuredById);
        builder.HasIndex(e => new { e.CharacteristicId, e.SubgroupNumber });

        builder.HasOne(e => e.Characteristic)
            .WithMany(c => c.Measurements)
            .HasForeignKey(e => e.CharacteristicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.MeasuredById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
