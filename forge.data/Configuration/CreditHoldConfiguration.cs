using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CreditHoldConfiguration : IEntityTypeConfiguration<CreditHold>
{
    public void Configure(EntityTypeBuilder<CreditHold> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.ReleaseNotes).HasMaxLength(2000);

        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.PlacedById);
        builder.HasIndex(e => e.IsActive);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
