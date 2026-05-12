using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.Property(e => e.Code).HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Symbol).HasMaxLength(20);
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.Category);
    }
}
