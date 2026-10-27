using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorCreditMemoLineConfiguration : IEntityTypeConfiguration<VendorCreditMemoLine>
{
    public void Configure(EntityTypeBuilder<VendorCreditMemoLine> builder)
    {
        builder.ToTable("vendor_credit_memo_lines");

        builder.Ignore(e => e.LineTotal);

        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.AccountDeterminationKey).HasMaxLength(50).HasDefaultValue("OPERATING_EXPENSE");

        builder.HasIndex(e => e.VendorCreditMemoId);
        builder.HasIndex(e => e.VendorBillLineId);
        builder.HasIndex(e => e.VendorReturnLineId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.GlAccountId);

        builder.HasOne(e => e.VendorBillLine)
            .WithMany()
            .HasForeignKey(e => e.VendorBillLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.VendorReturnLine)
            .WithMany()
            .HasForeignKey(e => e.VendorReturnLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Forge.Core.Entities.Accounting.GlAccount>()
            .WithMany()
            .HasForeignKey(e => e.GlAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
