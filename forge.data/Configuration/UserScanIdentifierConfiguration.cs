using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class UserScanIdentifierConfiguration : IEntityTypeConfiguration<UserScanIdentifier>
{
    public void Configure(EntityTypeBuilder<UserScanIdentifier> builder)
    {
        builder.HasIndex(x => new { x.IdentifierType, x.IdentifierValue })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(x => x.UserId);

        builder.Property(x => x.IdentifierType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IdentifierValue).HasMaxLength(200).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
