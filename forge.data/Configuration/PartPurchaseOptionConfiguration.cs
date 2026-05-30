using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PartPurchaseOptionConfiguration : IEntityTypeConfiguration<PartPurchaseOption>
{
    public void Configure(EntityTypeBuilder<PartPurchaseOption> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Label).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ContentQuantity).HasPrecision(18, 4);

        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany(p => p.PurchaseOptions)
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ContentUom)
            .WithMany()
            .HasForeignKey(e => e.ContentUomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
