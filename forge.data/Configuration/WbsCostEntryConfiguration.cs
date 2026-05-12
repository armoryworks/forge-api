using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WbsCostEntryConfiguration : IEntityTypeConfiguration<WbsCostEntry>
{
    public void Configure(EntityTypeBuilder<WbsCostEntry> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.SourceEntityType).HasMaxLength(50);

        builder.HasIndex(e => e.WbsElementId);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.EntryDate);
        builder.HasIndex(e => new { e.SourceEntityType, e.SourceEntityId });
    }
}
