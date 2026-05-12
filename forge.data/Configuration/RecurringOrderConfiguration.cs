using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RecurringOrderConfiguration : IEntityTypeConfiguration<RecurringOrder>
{
    public void Configure(EntityTypeBuilder<RecurringOrder> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.NextGenerationDate);

        builder.HasOne(e => e.Customer)
            .WithMany(c => c.RecurringOrders)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ShippingAddress)
            .WithMany()
            .HasForeignKey(e => e.ShippingAddressId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.RecurringOrder)
            .HasForeignKey(l => l.RecurringOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
