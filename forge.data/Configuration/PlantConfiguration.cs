using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PlantConfiguration : IEntityTypeConfiguration<Plant>
{
    public void Configure(EntityTypeBuilder<Plant> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.TimeZone).HasMaxLength(100);
        builder.Property(e => e.CurrencyCode).HasMaxLength(10);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.CompanyLocationId);
        builder.HasIndex(e => e.IsActive);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.CompanyLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.OutboundTransfers)
            .WithOne(t => t.FromPlant)
            .HasForeignKey(t => t.FromPlantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.InboundTransfers)
            .WithOne(t => t.ToPlant)
            .HasForeignKey(t => t.ToPlantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
