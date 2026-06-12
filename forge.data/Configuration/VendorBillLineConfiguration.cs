using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — VendorBillLine. The VendorBill→Lines relationship is defined on
/// <see cref="VendorBillConfiguration"/>; this sets the table, money precision, and the Part FK.
/// </summary>
public class VendorBillLineConfiguration : IEntityTypeConfiguration<VendorBillLine>
{
    public void Configure(EntityTypeBuilder<VendorBillLine> builder)
    {
        builder.ToTable("vendor_bill_lines");

        builder.Ignore(e => e.LineTotal);

        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.AccountDeterminationKey).HasMaxLength(64).IsRequired();

        builder.HasIndex(e => e.PartId).HasDatabaseName("ix_vendor_bill_lines_part");
        builder.HasIndex(e => e.PurchaseOrderLineId).HasDatabaseName("ix_vendor_bill_lines_po_line");

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .HasConstraintName("fk_vendor_bill_lines_part")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderLineId)
            .HasConstraintName("fk_vendor_bill_lines_po_line")
            .OnDelete(DeleteBehavior.SetNull);

        // Job-costing tag (carried to the GL debit line on standalone-bill posting).
        builder.HasIndex(e => e.JobId).HasDatabaseName("ix_vendor_bill_lines_job");
        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .HasConstraintName("fk_vendor_bill_lines_job")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
