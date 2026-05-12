using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CapabilityConfiguration : IEntityTypeConfiguration<Capability>
{
    public void Configure(EntityTypeBuilder<Capability> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();

        builder.Property(e => e.Area).HasMaxLength(16).IsRequired();
        builder.HasIndex(e => e.Area);

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Description).HasColumnType("text");

        builder.Property(e => e.Enabled).IsRequired();
        builder.Property(e => e.IsDefaultOn).IsRequired();

        builder.Property(e => e.RequiresRoles).HasMaxLength(256);

        // Postgres xmin acts as the row version for optimistic concurrency.
        builder.Property(e => e.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Phase 4 Phase-C — API-surfaced ETag value, bumped manually in
        // AppDbContext.SaveChangesAsync() per IConcurrencyVersioned contract.
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.HasMany(e => e.Configs)
            .WithOne(c => c.Capability)
            .HasForeignKey(c => c.CapabilityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
