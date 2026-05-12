using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EntityReadinessValidatorConfiguration : IEntityTypeConfiguration<EntityReadinessValidator>
{
    public void Configure(EntityTypeBuilder<EntityReadinessValidator> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        // Surrogate key on Id (BaseEntity); the natural composite key is
        // unique-indexed so admin upserts can target (EntityType, ValidatorId)
        // without collapsing the FK story used elsewhere in the codebase.
        builder.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ValidatorId).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => new { e.EntityType, e.ValidatorId }).IsUnique();
        builder.HasIndex(e => e.EntityType);

        builder.Property(e => e.Predicate)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ApplicabilityPredicate)
            .HasColumnType("jsonb");

        builder.Property(e => e.DisplayNameKey).HasMaxLength(128).IsRequired();
        builder.Property(e => e.MissingMessageKey).HasMaxLength(128).IsRequired();
        builder.Property(e => e.IsSeedData).IsRequired();
    }
}
