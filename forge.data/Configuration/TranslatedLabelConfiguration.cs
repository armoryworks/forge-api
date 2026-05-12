using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TranslatedLabelConfiguration : IEntityTypeConfiguration<TranslatedLabel>
{
    public void Configure(EntityTypeBuilder<TranslatedLabel> builder)
    {
        builder.Property(e => e.Key).HasMaxLength(200).IsRequired();
        builder.Property(e => e.LanguageCode).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Value).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.Context).HasMaxLength(100);

        builder.HasIndex(e => new { e.Key, e.LanguageCode }).IsUnique();
        builder.HasIndex(e => e.LanguageCode);
        builder.HasIndex(e => e.TranslatedById);
    }
}
