using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Url).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.EncryptedSecret).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => e.IsActive);

        builder.HasMany(e => e.Deliveries)
            .WithOne(d => d.Subscription)
            .HasForeignKey(d => d.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
