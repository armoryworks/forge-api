using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CreditMemoApplicationConfiguration : IEntityTypeConfiguration<CreditMemoApplication>
{
    public void Configure(EntityTypeBuilder<CreditMemoApplication> builder)
    {
        builder.ToTable("credit_memo_applications");

        builder.Ignore(e => e.IsLive);

        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasIndex(e => e.CreditMemoId);
        builder.HasIndex(e => e.InvoiceId);
    }
}
