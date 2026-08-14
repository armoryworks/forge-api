using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RetailBuyerConfiguration : IEntityTypeConfiguration<RetailBuyer>
{
    public void Configure(EntityTypeBuilder<RetailBuyer> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.ExternalBuyerId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ContactEmail).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);

        builder.HasOne(e => e.Channel)
            .WithMany(c => c.RetailBuyers)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        // The identity key an import matches on to recognize a repeat buyer.
        // Scoped to the channel because the same person on eBay and Etsy is two
        // unrelated external ids — cross-channel identity resolution is a
        // separate problem and deliberately not solved by this key.
        builder.HasIndex(e => new { e.ChannelId, e.ExternalBuyerId }).IsUnique();

        builder.HasIndex(e => e.ContactEmail);

        // Drives the PII purge sweep; partial so it stays small on installs
        // that never set a retention window.
        builder.HasIndex(e => e.PurgeAfter).HasFilter("purge_after IS NOT NULL");
    }
}
