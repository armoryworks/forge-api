using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class NonConformanceConfiguration : IEntityTypeConfiguration<NonConformance>
{
    public void Configure(EntityTypeBuilder<NonConformance> builder)
    {
        builder.Property(e => e.NcrNumber).HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.ContainmentActions).HasMaxLength(4000);
        builder.Property(e => e.DispositionNotes).HasMaxLength(4000);
        builder.Property(e => e.ReworkInstructions).HasMaxLength(4000);
        builder.Property(e => e.AffectedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.DefectiveQuantity).HasPrecision(18, 4);
        builder.Property(e => e.MaterialCost).HasPrecision(18, 2);
        builder.Property(e => e.LaborCost).HasPrecision(18, 2);
        builder.Property(e => e.TotalCostImpact).HasPrecision(18, 2);

        builder.HasIndex(e => e.NcrNumber).IsUnique();
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.CapaId);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.DetectedById);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.QcInspectionId);
        builder.HasIndex(e => e.ProductionRunId);
        builder.HasIndex(e => e.SalesOrderLineId);
        builder.HasIndex(e => e.PurchaseOrderLineId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Capa)
            .WithMany(c => c.RelatedNcrs)
            .HasForeignKey(e => e.CapaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK-only ApplicationUser references
        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.DetectedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.ContainmentById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.DispositionById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
