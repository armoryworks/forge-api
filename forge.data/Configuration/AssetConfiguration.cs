using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        // Phase 3 H2 / WU-12: IActiveAware contract member — derived from Status.
        builder.Ignore(e => e.IsActiveForNewTransactions);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.Property(e => e.Manufacturer).HasMaxLength(200);
        builder.Property(e => e.Model).HasMaxLength(200);
        builder.Property(e => e.SerialNumber).HasMaxLength(100);
        builder.Property(e => e.PhotoFileId).HasMaxLength(200);
        builder.Property(e => e.CurrentHours).HasPrecision(18, 2);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.SerialNumber);
        builder.HasIndex(e => e.SourceJobId);
        builder.HasIndex(e => e.SourcePartId);

        builder.HasOne(e => e.SourceJob)
            .WithMany()
            .HasForeignKey(e => e.SourceJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SourcePart)
            .WithMany()
            .HasForeignKey(e => e.SourcePartId)
            .OnDelete(DeleteBehavior.SetNull);

        // Phase 3 F4 — full-record fields. AcquisitionCost stored at money
        // precision (18,2); GlAccount bounded to 100 chars same as the
        // sales-tax GL posting field.
        builder.Property(e => e.AcquisitionCost).HasPrecision(18, 2);
        builder.Property(e => e.GlAccount).HasMaxLength(100);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.WorkCenterId);
    }
}
