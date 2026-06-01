using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class OperationMaterialConfiguration : IEntityTypeConfiguration<OperationMaterial>
{
    public void Configure(EntityTypeBuilder<OperationMaterial> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasIndex(e => e.OperationId);
        builder.HasIndex(e => e.BomLineId);

        builder.HasOne(e => e.Operation)
            .WithMany(o => o.Materials)
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.BomLine)
            .WithMany()
            .HasForeignKey(e => e.BomLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
