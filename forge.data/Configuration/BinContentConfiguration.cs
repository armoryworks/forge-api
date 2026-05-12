using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BinContentConfiguration : IEntityTypeConfiguration<BinContent>
{
    public void Configure(EntityTypeBuilder<BinContent> builder)
    {
        builder.Property(e => e.EntityType).HasMaxLength(50);
        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.ReservedQuantity).HasPrecision(18, 4);

        builder.HasOne(e => e.Location)
            .WithMany(e => e.Contents)
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.LocationId, e.EntityType, e.EntityId });
    }
}
