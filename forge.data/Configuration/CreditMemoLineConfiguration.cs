using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CreditMemoLineConfiguration : IEntityTypeConfiguration<CreditMemoLine>
{
    public void Configure(EntityTypeBuilder<CreditMemoLine> builder)
    {
        builder.ToTable("credit_memo_lines");

        builder.Ignore(e => e.LineTotal);
        builder.Ignore(e => e.SignedTotal);
        builder.Ignore(e => e.IsTaxable);

        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.TaxCode).HasMaxLength(10);

        builder.HasIndex(e => e.CreditMemoId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.InvoiceLineId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.InvoiceLine)
            .WithMany()
            .HasForeignKey(e => e.InvoiceLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
