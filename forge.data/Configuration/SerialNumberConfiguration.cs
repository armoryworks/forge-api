using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
{
    public void Configure(EntityTypeBuilder<SerialNumber> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.SerialValue).IsUnique();
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.LotRecordId);
        builder.HasIndex(e => e.CurrentLocationId);
        builder.HasIndex(e => e.ShipmentLineId);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.ParentSerialId);
        builder.HasIndex(e => e.Status);

        builder.Property(e => e.SerialValue).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.Part)
            .WithMany(p => p.SerialNumbers)
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CurrentLocation)
            .WithMany()
            .HasForeignKey(e => e.CurrentLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ParentSerial)
            .WithMany(e => e.ChildSerials)
            .HasForeignKey(e => e.ParentSerialId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
