using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

public class CustomerPortalAccessConfiguration : IEntityTypeConfiguration<CustomerPortalAccess>
{
    public void Configure(EntityTypeBuilder<CustomerPortalAccess> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.OneTimeTokenHash).HasMaxLength(128);

        // Filtered unique index — one active row per Contact (soft-deleted
        // rows can co-exist).
        builder.HasIndex(e => e.ContactId)
            .IsUnique()
            .HasFilter(@"deleted_at IS NULL");

        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.OneTimeTokenHash);

        builder.HasOne(e => e.Contact)
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
