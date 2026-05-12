using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CycleCountConfiguration : IEntityTypeConfiguration<CycleCount>
{
    public void Configure(EntityTypeBuilder<CycleCount> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Status).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.CountedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.LocationId, e.CountedAt });
        builder.HasIndex(e => e.CountedById);
    }
}
