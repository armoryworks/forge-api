using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BomRevisionConfiguration : IEntityTypeConfiguration<BomRevision>
{
    public void Configure(EntityTypeBuilder<BomRevision> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => new { e.PartId, e.RevisionNumber }).IsUnique();
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany(p => p.BomRevisions)
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Entries)
            .WithOne(e => e.BomRevision)
            .HasForeignKey(e => e.BomRevisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BomRevisionEntryConfiguration : IEntityTypeConfiguration<BomRevisionEntry>
{
    public void Configure(EntityTypeBuilder<BomRevisionEntry> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitOfMeasure).HasMaxLength(50);
        builder.Property(e => e.ReferenceDesignator).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => e.BomRevisionId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
