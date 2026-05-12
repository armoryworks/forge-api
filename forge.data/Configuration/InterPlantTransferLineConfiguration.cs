using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class InterPlantTransferLineConfiguration : IEntityTypeConfiguration<InterPlantTransferLine>
{
    public void Configure(EntityTypeBuilder<InterPlantTransferLine> builder)
    {
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.ReceivedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.LotNumber).HasMaxLength(100);

        builder.HasIndex(e => e.TransferId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
