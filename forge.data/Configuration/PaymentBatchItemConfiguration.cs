using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>⚡ BANKING BOUNDARY — PaymentBatchItem (one NACHA entry-detail line).</summary>
public class PaymentBatchItemConfiguration : IEntityTypeConfiguration<PaymentBatchItem>
{
    public void Configure(EntityTypeBuilder<PaymentBatchItem> builder)
    {
        builder.ToTable("payment_batch_items");

        builder.Property(e => e.Amount).HasPrecision(18, 4);
        builder.Property(e => e.TraceNumber).HasMaxLength(15);

        // A live payment may appear in at most ONE non-cancelled batch; enforced in the
        // service (status lives on the parent, so a partial index can't express it) —
        // this plain index serves the membership lookups.
        builder.HasIndex(e => e.VendorPaymentId).HasDatabaseName("ix_payment_batch_items_payment");
        builder.HasIndex(e => e.VendorBankAccountId).HasDatabaseName("ix_payment_batch_items_account");

        builder.HasOne(e => e.VendorPayment)
            .WithMany()
            .HasForeignKey(e => e.VendorPaymentId)
            .HasConstraintName("fk_payment_batch_items_payment")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.VendorBankAccount)
            .WithMany()
            .HasForeignKey(e => e.VendorBankAccountId)
            .HasConstraintName("fk_payment_batch_items_account")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
