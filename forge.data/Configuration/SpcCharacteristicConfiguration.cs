using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SpcCharacteristicConfiguration : IEntityTypeConfiguration<SpcCharacteristic>
{
    public void Configure(EntityTypeBuilder<SpcCharacteristic> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.UnitOfMeasure).HasMaxLength(50);
        builder.Property(e => e.SampleFrequency).HasMaxLength(200);
        builder.Property(e => e.NominalValue).HasPrecision(18, 6);
        builder.Property(e => e.UpperSpecLimit).HasPrecision(18, 6);
        builder.Property(e => e.LowerSpecLimit).HasPrecision(18, 6);

        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.OperationId);
        builder.HasIndex(e => e.GageId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Operation)
            .WithMany()
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
