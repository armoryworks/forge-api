using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChannelListingConfiguration : IEntityTypeConfiguration<ChannelListing>
{
    public void Configure(EntityTypeBuilder<ChannelListing> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.ExternalListingId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ExternalSku).HasMaxLength(200);
        builder.Property(e => e.Title).HasMaxLength(500);
        builder.Property(e => e.ListedPrice).HasPrecision(18, 4);
        builder.Property(e => e.PublishedQuantity).HasPrecision(18, 4);

        builder.HasOne(e => e.Channel)
            .WithMany(c => c.Listings)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.ChannelId, e.ExternalListingId }).IsUnique();

        // Order import resolves a line by (channel, sku); inventory sync walks
        // the reverse edge from part to its listings.
        builder.HasIndex(e => new { e.ChannelId, e.ExternalSku });
        builder.HasIndex(e => e.PartId);
    }
}
