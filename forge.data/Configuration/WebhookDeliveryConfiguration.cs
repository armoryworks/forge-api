using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.Property(e => e.EventType).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.DurationMs).HasPrecision(18, 2);

        builder.HasIndex(e => e.SubscriptionId);
        builder.HasIndex(e => e.AttemptedAt);
        builder.HasIndex(e => e.IsSuccess);

        builder.HasOne(e => e.Subscription)
            .WithMany(s => s.Deliveries)
            .HasForeignKey(e => e.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
