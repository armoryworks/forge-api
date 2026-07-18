using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BarcodeConfiguration : IEntityTypeConfiguration<Barcode>
{
    public void Configure(EntityTypeBuilder<Barcode> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Value).HasMaxLength(500);
        builder.Property(e => e.EntityType).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.IdentityType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.Value).IsUnique();
        builder.HasIndex(e => e.EntityType);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.SalesOrderId);
        builder.HasIndex(e => e.PurchaseOrderId);
        builder.HasIndex(e => e.AssetId);
        builder.HasIndex(e => e.StorageLocationId);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SalesOrder)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.StorageLocation)
            .WithMany()
            .HasForeignKey(e => e.StorageLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
