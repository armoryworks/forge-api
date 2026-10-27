using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>⚡ ACCOUNTING BOUNDARY — persisted payment runs, their picked bills and their vendor credit deductions.</summary>
public class PaymentRunConfiguration : IEntityTypeConfiguration<PaymentRun>
{
    public void Configure(EntityTypeBuilder<PaymentRun> builder)
    {
        builder.ToTable("payment_runs");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.RunNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Method).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ReferenceNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.ApplyVendorCredits).HasDefaultValue(true);

        builder.HasIndex(e => e.RunNumber).IsUnique().HasDatabaseName("ux_payment_runs_run_number");
        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Items)
            .WithOne(i => i.PaymentRun)
            .HasForeignKey(i => i.PaymentRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Credits)
            .WithOne(c => c.PaymentRun)
            .HasForeignKey(c => c.PaymentRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
