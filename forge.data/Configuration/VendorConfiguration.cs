using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        // Phase 3 H2 / WU-12: IActiveAware contract member — pure compute over
        // IsActive; EF should not see it as a column.
        builder.Ignore(e => e.IsActiveForNewTransactions);

        builder.Property(e => e.CompanyName).HasMaxLength(200);
        builder.Property(e => e.ContactName).HasMaxLength(200);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Address).HasMaxLength(500);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.State).HasMaxLength(100);
        builder.Property(e => e.ZipCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(100);
        builder.Property(e => e.PaymentTerms).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.AutoPoMode)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(e => e.MinOrderAmount).HasPrecision(18, 4);
        // Bought-parts effort PR4 — variance pct stored to 2dp (e.g. 7.50 = 7.5%).
        builder.Property(e => e.OffTierVariancePct).HasPrecision(6, 2);

        builder.HasIndex(e => e.CompanyName);
    }
}
