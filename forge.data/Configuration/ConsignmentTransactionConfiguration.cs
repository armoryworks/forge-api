using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ConsignmentTransactionConfiguration : IEntityTypeConfiguration<ConsignmentTransaction>
{
    public void Configure(EntityTypeBuilder<ConsignmentTransaction> builder)
    {
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.ExtendedAmount).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.AgreementId);
        builder.HasIndex(e => e.PurchaseOrderId);
        builder.HasIndex(e => e.InvoiceId);

        builder.HasOne(e => e.PurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Invoice)
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
