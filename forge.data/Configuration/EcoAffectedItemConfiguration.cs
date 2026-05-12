using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EcoAffectedItemConfiguration : IEntityTypeConfiguration<EcoAffectedItem>
{
    public void Configure(EntityTypeBuilder<EcoAffectedItem> builder)
    {
        builder.Property(e => e.EntityType).HasMaxLength(50);
        builder.Property(e => e.ChangeDescription).HasMaxLength(500);
        builder.Property(e => e.OldValue).HasColumnType("jsonb");
        builder.Property(e => e.NewValue).HasColumnType("jsonb");

        builder.HasIndex(e => e.EcoId);

        builder.HasOne(e => e.Eco)
            .WithMany(eco => eco.AffectedItems)
            .HasForeignKey(e => e.EcoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
