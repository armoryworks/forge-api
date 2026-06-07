using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class FixedAssetConfiguration : IEntityTypeConfiguration<FixedAsset>
{
    public void Configure(EntityTypeBuilder<FixedAsset> builder)
    {
        builder.ToTable("acct_fixed_assets");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.DepreciableBase);
        builder.Ignore(e => e.MonthlyStraightLine);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.AssetTag).HasMaxLength(60);
        builder.Property(e => e.Cost).HasPrecision(18, 2);
        builder.Property(e => e.SalvageValue).HasPrecision(18, 2);
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(e => e.BookId).HasDatabaseName("ix_acct_fixed_assets_book");

        builder.HasMany(e => e.DepreciationEntries)
            .WithOne(d => d.FixedAsset)
            .HasForeignKey(d => d.FixedAssetId)
            .HasConstraintName("fk_acct_depreciation_entries_asset")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DepreciationEntryConfiguration : IEntityTypeConfiguration<DepreciationEntry>
{
    public void Configure(EntityTypeBuilder<DepreciationEntry> builder)
    {
        builder.ToTable("acct_depreciation_entries");

        builder.Property(e => e.Amount).HasPrecision(18, 2);

        // One depreciation posting per asset per month (idempotency backstop).
        builder.HasIndex(e => new { e.FixedAssetId, e.PeriodMonth })
            .IsUnique()
            .HasDatabaseName("ux_acct_depreciation_entries_asset_month");
    }
}
