using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SalesChannelConfiguration : IEntityTypeConfiguration<SalesChannel>
{
    public void Configure(EntityTypeBuilder<SalesChannel> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.IsRetail);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Code).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.ChannelType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TaxCollectedBy).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.OrderNumberPrefix).HasMaxLength(10);

        builder.HasOne(e => e.SoldToCustomer)
            .WithMany()
            .HasForeignKey(e => e.SoldToCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ECommerceIntegration)
            .WithMany()
            .HasForeignKey(e => e.ECommerceIntegrationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.ChannelType);
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.SoldToCustomerId);
        builder.HasIndex(e => e.ECommerceIntegrationId);

        // Exactly one default channel, enforced in the database rather than by
        // handler discipline — a null SalesOrder.ChannelId resolves through it,
        // so two defaults would make order routing ambiguous. Mirrors
        // CompanyLocationConfiguration's filtered unique index.
        builder.HasIndex(e => e.IsDefault)
            .HasFilter("is_default = true")
            .IsUnique();
    }
}
