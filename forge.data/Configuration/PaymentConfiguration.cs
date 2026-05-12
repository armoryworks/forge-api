using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Payment entity exists in standalone mode; read-only cache in integrated mode.
/// </summary>
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.AppliedAmount);
        builder.Ignore(e => e.UnappliedAmount);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.Property(e => e.PaymentNumber).HasMaxLength(20);
        builder.Property(e => e.ReferenceNumber).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Amount).HasPrecision(18, 4);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        builder.HasIndex(e => e.PaymentNumber).IsUnique();
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.Method);

        builder.HasOne(e => e.Customer)
            .WithMany(c => c.Payments)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Applications)
            .WithOne(a => a.Payment)
            .HasForeignKey(a => a.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
