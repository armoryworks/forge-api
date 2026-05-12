using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EntityCapabilityRequirementConfiguration : IEntityTypeConfiguration<EntityCapabilityRequirement>
{
    public void Configure(EntityTypeBuilder<EntityCapabilityRequirement> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.CapabilityCode).HasMaxLength(64).IsRequired();
        builder.Property(e => e.RequirementId).HasMaxLength(64).IsRequired();

        // Composite uniqueness on the natural key — admin upserts target this
        // tuple. Lookup hot paths are (EntityType, CapabilityCode) for chip
        // evaluation and EntityType alone for the admin list filter; both
        // are covered by the unique index plus the secondary single-column
        // index below.
        builder.HasIndex(e => new { e.EntityType, e.CapabilityCode, e.RequirementId }).IsUnique();
        builder.HasIndex(e => e.EntityType);

        builder.Property(e => e.Predicate)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.DisplayNameKey).HasMaxLength(128).IsRequired();
        builder.Property(e => e.MissingMessageKey).HasMaxLength(128).IsRequired();
        builder.Property(e => e.SortOrder).IsRequired();
        builder.Property(e => e.IsSeedData).IsRequired();
    }
}
