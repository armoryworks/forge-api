using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class InterPlantTransferConfiguration : IEntityTypeConfiguration<InterPlantTransfer>
{
    public void Configure(EntityTypeBuilder<InterPlantTransfer> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.TransferNumber).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TrackingNumber).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.TransferNumber).IsUnique();
        builder.HasIndex(e => e.FromPlantId);
        builder.HasIndex(e => e.ToPlantId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ShippedById);
        builder.HasIndex(e => e.ReceivedById);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Transfer)
            .HasForeignKey(l => l.TransferId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
