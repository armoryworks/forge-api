using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentApplicationConfiguration : IEntityTypeConfiguration<PaymentApplication>
{
    public void Configure(EntityTypeBuilder<PaymentApplication> builder)
    {
        builder.Property(e => e.Amount).HasPrecision(18, 4);

        builder.HasIndex(e => e.PaymentId);
        builder.HasIndex(e => e.InvoiceId);
    }
}
