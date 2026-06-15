using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SalesTaxRateConfiguration : IEntityTypeConfiguration<SalesTaxRate>
{
    public void Configure(EntityTypeBuilder<SalesTaxRate> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.ExemptFlag).HasDefaultValueSql("false");
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Code).HasMaxLength(20);
        builder.Property(e => e.StateCode).HasMaxLength(2);
        builder.Property(e => e.Rate).HasPrecision(8, 6);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.StateCode);
        builder.HasIndex(e => new { e.StateCode, e.EffectiveTo });

        // Phase 3 F5 — full-record fields. GlPostingAccount bounded to 100 chars.
        builder.Property(e => e.GlPostingAccount).HasMaxLength(100);
    }
}
