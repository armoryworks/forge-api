using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class KanbanCardConfiguration : IEntityTypeConfiguration<KanbanCard>
{
    public void Configure(EntityTypeBuilder<KanbanCard> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.CardNumber).HasMaxLength(20);
        builder.Property(e => e.ActiveOrderType).HasMaxLength(50);
        builder.Property(e => e.BinQuantity).HasPrecision(18, 4);
        builder.Property(e => e.LeadTimeDays).HasPrecision(10, 2);

        builder.HasIndex(e => e.CardNumber).IsUnique();
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.IsActive);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.StorageLocation)
            .WithMany()
            .HasForeignKey(e => e.StorageLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SupplyVendor)
            .WithMany()
            .HasForeignKey(e => e.SupplyVendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.TriggerLogs)
            .WithOne(e => e.KanbanCard)
            .HasForeignKey(e => e.KanbanCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
