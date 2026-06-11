using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentTransmissionConfiguration : IEntityTypeConfiguration<PaymentTransmission>
{
    public void Configure(EntityTypeBuilder<PaymentTransmission> builder)
    {
        builder.ToTable("payment_transmissions");

        builder.Property(t => t.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(t => t.Method).HasMaxLength(30).IsRequired();
        builder.Property(t => t.LastError).HasMaxLength(4000);
        builder.Property(t => t.SubmissionRef).HasMaxLength(200);
        builder.Property(t => t.Amount).HasPrecision(18, 2);

        // Lookup by source document (latest-per-payment projection on the AP lists)
        builder.HasIndex(t => new { t.SourceType, t.SourceId })
            .HasDatabaseName("ix_payment_transmissions_source");

        // Triage filter (Failed rows surfaced for manual reprocessing)
        builder.HasIndex(t => t.Status)
            .HasDatabaseName("ix_payment_transmissions_status");
    }
}
