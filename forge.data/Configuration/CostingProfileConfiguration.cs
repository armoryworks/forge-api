using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CostingProfileConfiguration : IEntityTypeConfiguration<CostingProfile>
{
    public void Configure(EntityTypeBuilder<CostingProfile> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();

        builder.Property(e => e.Mode).HasMaxLength(16).IsRequired();

        builder.Property(e => e.FlatRatePct).HasColumnType("decimal(7,4)");

        builder.Property(e => e.DepartmentalRates).HasColumnType("jsonb");
        builder.Property(e => e.Pools).HasColumnType("jsonb");

        builder.Property(e => e.EffectiveFrom).IsRequired();
    }
}
