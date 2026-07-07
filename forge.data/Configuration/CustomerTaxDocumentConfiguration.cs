using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CustomerTaxDocumentConfiguration : IEntityTypeConfiguration<CustomerTaxDocument>
{
    public void Configure(EntityTypeBuilder<CustomerTaxDocument> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ParsedFields).HasColumnType("jsonb");
        builder.HasIndex(e => e.CustomerId);
    }
}
