using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class PayRunConfiguration : IEntityTypeConfiguration<PayRun>
{
    public void Configure(EntityTypeBuilder<PayRun> builder)
    {
        builder.ToTable("acct_pay_runs");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.NetPay);

        builder.Property(e => e.GrossWages).HasPrecision(18, 2);
        builder.Property(e => e.EmployeeTaxWithheld).HasPrecision(18, 2);
        builder.Property(e => e.EmployerTax).HasPrecision(18, 2);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(e => e.BookId).HasDatabaseName("ix_acct_pay_runs_book");
    }
}
