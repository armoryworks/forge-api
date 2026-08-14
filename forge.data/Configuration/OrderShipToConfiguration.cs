using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class OrderShipToConfiguration : IEntityTypeConfiguration<OrderShipTo>
{
    public void Configure(EntityTypeBuilder<OrderShipTo> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Company).HasMaxLength(200);
        builder.Property(e => e.Line1).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Line2).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100).IsRequired();
        builder.Property(e => e.State).HasMaxLength(100).IsRequired();
        builder.Property(e => e.PostalCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Country).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Phone).HasMaxLength(50);

        // 1:1 with the order. Cascade because the snapshot has no meaning
        // without its order — unlike CustomerAddress, nothing else references it.
        builder.HasOne(e => e.SalesOrder)
            .WithOne(o => o.ShipTo)
            .HasForeignKey<OrderShipTo>(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.SalesOrderId).IsUnique();
    }
}
