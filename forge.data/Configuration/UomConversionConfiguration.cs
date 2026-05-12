using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class UomConversionConfiguration : IEntityTypeConfiguration<UomConversion>
{
    public void Configure(EntityTypeBuilder<UomConversion> builder)
    {
        builder.Property(e => e.ConversionFactor).HasPrecision(18, 8);

        builder.HasIndex(e => new { e.FromUomId, e.ToUomId, e.PartId }).IsUnique();

        builder.HasOne(e => e.FromUom)
            .WithMany(u => u.ConversionsFrom)
            .HasForeignKey(e => e.FromUomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToUom)
            .WithMany(u => u.ConversionsTo)
            .HasForeignKey(e => e.ToUomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
