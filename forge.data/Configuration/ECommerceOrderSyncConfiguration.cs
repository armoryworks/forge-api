using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ECommerceOrderSyncConfiguration : IEntityTypeConfiguration<ECommerceOrderSync>
{
    public void Configure(EntityTypeBuilder<ECommerceOrderSync> builder)
    {
        builder.Property(e => e.ExternalOrderId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ExternalOrderNumber).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.OrderDataJson).HasColumnType("jsonb").IsRequired();

        builder.HasOne(e => e.Integration)
            .WithMany(i => i.OrderSyncs)
            .HasForeignKey(e => e.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SalesOrder)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.IntegrationId);
        builder.HasIndex(e => e.SalesOrderId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.IntegrationId, e.ExternalOrderId }).IsUnique();
    }
}
