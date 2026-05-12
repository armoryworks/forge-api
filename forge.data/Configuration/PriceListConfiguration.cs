using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => e.CustomerId);

        builder.HasOne(e => e.Customer)
            .WithMany(c => c.PriceLists)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Entries)
            .WithOne(e => e.PriceList)
            .HasForeignKey(e => e.PriceListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
