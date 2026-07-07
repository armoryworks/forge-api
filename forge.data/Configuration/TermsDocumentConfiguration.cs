using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TermsDocumentConfiguration : IEntityTypeConfiguration<TermsDocument>
{
    public void Configure(EntityTypeBuilder<TermsDocument> builder)
    {
        builder.Property(e => e.Scope).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.PartId);
    }
}
