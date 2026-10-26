using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentTermConfiguration : IEntityTypeConfiguration<PaymentTerm>
{
    public void Configure(EntityTypeBuilder<PaymentTerm> builder)
    {
        builder.ToTable("payment_terms");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.HasDiscount);

        builder.Property(e => e.Code).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DiscountPercent).HasPrecision(7, 4);

        builder.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_payment_terms_code");
        builder.HasIndex(e => e.LegacyCreditTerms).IsUnique().HasDatabaseName("ux_payment_terms_legacy_credit_terms");
    }
}
