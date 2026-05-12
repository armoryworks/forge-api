using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PartPriceConfiguration : IEntityTypeConfiguration<PartPrice>
{
    public void Configure(EntityTypeBuilder<PartPrice> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.UnitPrice)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasMaxLength(3)
            .IsRequired()
            .HasDefaultValue("USD");

        builder.Property(p => p.EffectiveFrom)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.Notes)
            .HasMaxLength(500);

        builder.HasOne(p => p.Part)
            .WithMany()
            .HasForeignKey(p => p.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.PartId);
        builder.HasIndex(p => new { p.PartId, p.EffectiveTo });
    }
}
