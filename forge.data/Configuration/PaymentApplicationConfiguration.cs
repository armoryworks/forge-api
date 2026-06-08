using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentApplicationConfiguration : IEntityTypeConfiguration<PaymentApplication>
{
    public void Configure(EntityTypeBuilder<PaymentApplication> builder)
    {
        builder.Property(e => e.Amount).HasPrecision(18, 4);
        // Settlement FX rate (Phase-4 FULLGL, additive). Default 1 so existing rows backfill to unity —
        // the single-currency settlement path is byte-for-byte unchanged. Precision matches JournalLine.FxRate.
        builder.Property(e => e.SettlementFxRate).HasPrecision(18, 8).HasDefaultValue(1m);

        builder.HasIndex(e => e.PaymentId);
        builder.HasIndex(e => e.InvoiceId);
    }
}
