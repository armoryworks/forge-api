using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

/// <summary>⚡ PAY-001 — per-employee register row under a pay run (owner-ratified granularity).</summary>
public class PayRunLineConfiguration : IEntityTypeConfiguration<PayRunLine>
{
    public void Configure(EntityTypeBuilder<PayRunLine> builder)
    {
        builder.ToTable("acct_pay_run_lines");

        builder.Ignore(e => e.TotalWithholdings);

        builder.Property(e => e.EmployeeName).HasMaxLength(120).IsRequired();
        builder.Property(e => e.GrossPay).HasPrecision(18, 2);
        builder.Property(e => e.FederalWithholding).HasPrecision(18, 2);
        builder.Property(e => e.StateWithholding).HasPrecision(18, 2);
        builder.Property(e => e.FicaEmployee).HasPrecision(18, 2);
        builder.Property(e => e.OtherDeductions).HasPrecision(18, 2);
        builder.Property(e => e.EmployerTax).HasPrecision(18, 2);
        builder.Property(e => e.NetPay).HasPrecision(18, 2);

        builder.HasIndex(e => e.PayRunId).HasDatabaseName("ix_acct_pay_run_lines_run");

        builder.HasOne(e => e.PayRun)
            .WithMany(r => r.Lines)
            .HasForeignKey(e => e.PayRunId)
            .HasConstraintName("fk_acct_pay_run_lines_run")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
