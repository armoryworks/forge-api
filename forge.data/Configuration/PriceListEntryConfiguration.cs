using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PriceListEntryConfiguration : IEntityTypeConfiguration<PriceListEntry>
{
    public void Configure(EntityTypeBuilder<PriceListEntry> builder)
    {
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);

        builder.Property(e => e.Currency)
            .HasMaxLength(3)
            .IsRequired()
            .HasDefaultValue("USD");

        builder.Property(e => e.Notes)
            .HasMaxLength(2000);

        builder.HasIndex(e => e.PriceListId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => new { e.PriceListId, e.PartId, e.MinQuantity }).IsUnique();

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
