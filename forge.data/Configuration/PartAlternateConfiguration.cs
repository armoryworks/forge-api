using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PartAlternateConfiguration : IEntityTypeConfiguration<PartAlternate>
{
    public void Configure(EntityTypeBuilder<PartAlternate> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.ConversionFactor).HasPrecision(18, 6);

        builder.HasIndex(e => new { e.PartId, e.AlternatePartId }).IsUnique();
        builder.HasIndex(e => e.AlternatePartId);
        builder.HasIndex(e => e.ApprovedById);

        builder.HasOne(e => e.Part)
            .WithMany(p => p.Alternates)
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.AlternatePart)
            .WithMany()
            .HasForeignKey(e => e.AlternatePartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
