using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Identity.Configuration;

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
            // Pin pre-move constraint name (assembly relocation shifted EF's
            // default FK-name generation — keep the live DB constraint stable).
            .HasConstraintName("fk_user_scan_identifiers__asp_net_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
