using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class I18nLabelOverrideConfiguration : IEntityTypeConfiguration<I18nLabelOverride>
{
    public void Configure(EntityTypeBuilder<I18nLabelOverride> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Key).HasMaxLength(400).IsRequired();
        builder.Property(e => e.LanguageCode).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Value).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.SourceLanguageCode).HasMaxLength(10);

        // One live override per key + language; soft-deleted rows don't block re-creation.
        builder.HasIndex(e => new { e.Key, e.LanguageCode })
            .IsUnique()
            .HasFilter(@"deleted_at IS NULL");
    }
}
