using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> builder)
    {
        builder.ToTable("acct_fiscal_periods");

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Optimistic-locking token guarding close-vs-post races (§5.1, §9).
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.HasIndex(e => new { e.FiscalYearId, e.PeriodNumber })
            .IsUnique()
            .HasDatabaseName("ux_acct_fiscal_periods_year_number");

        builder.HasOne(e => e.FiscalYear)
            .WithMany(fy => fy.Periods)
            .HasForeignKey(e => e.FiscalYearId)
            .HasConstraintName("fk_acct_fiscal_periods_year")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
