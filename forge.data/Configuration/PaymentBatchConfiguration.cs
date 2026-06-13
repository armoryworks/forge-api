using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>⚡ BANKING BOUNDARY — PaymentBatch (one NACHA file). Status as string (ops greppability).</summary>
public class PaymentBatchConfiguration : IEntityTypeConfiguration<PaymentBatch>
{
    public void Configure(EntityTypeBuilder<PaymentBatch> builder)
    {
        builder.ToTable("payment_batches");

        builder.Property(e => e.BatchNumber).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TotalAmount).HasPrecision(18, 4);
        // The whole generated NACHA file (94 chars × lines) — text, unbounded.
        builder.Property(e => e.FileContents);

        builder.HasIndex(e => e.BatchNumber).IsUnique().HasDatabaseName("ux_payment_batches_number");
        builder.HasIndex(e => e.Status).HasDatabaseName("ix_payment_batches_status");

        builder.HasMany(e => e.Items)
            .WithOne(i => i.PaymentBatch)
            .HasForeignKey(i => i.PaymentBatchId)
            .HasConstraintName("fk_payment_batch_items_batch")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
