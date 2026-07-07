using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class QuoteTermsSnapshotConfiguration : IEntityTypeConfiguration<QuoteTermsSnapshot>
{
    public void Configure(EntityTypeBuilder<QuoteTermsSnapshot> builder)
    {
        builder.Property(e => e.CompiledSource).HasColumnType("jsonb");
        builder.HasIndex(e => e.AccessToken).IsUnique();
        builder.HasIndex(e => e.QuoteId);
    }
}
