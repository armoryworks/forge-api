using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CustomerReturnConfiguration : IEntityTypeConfiguration<CustomerReturn>
{
    public void Configure(EntityTypeBuilder<CustomerReturn> builder)
    {
        builder.Property(e => e.ReturnNumber).HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.InspectionNotes).HasMaxLength(2000);

        builder.Property(e => e.ExternalRmaId).HasMaxLength(200);
        builder.Property(e => e.RefundAmount).HasPrecision(18, 2);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        builder.HasIndex(e => e.ReturnNumber).IsUnique();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.SalesOrderLineId);
        builder.HasIndex(e => e.ChannelId);

        // Idempotency for channel-originated returns: a marketplace RMA must
        // import once no matter how often the poll replays it.
        builder.HasIndex(e => new { e.ChannelId, e.ExternalRmaId })
            .IsUnique()
            .HasFilter("channel_id IS NOT NULL AND external_rma_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OriginalJob)
            .WithMany()
            .HasForeignKey(e => e.OriginalJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SalesOrderLine)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReworkJob)
            .WithMany()
            .HasForeignKey(e => e.ReworkJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
