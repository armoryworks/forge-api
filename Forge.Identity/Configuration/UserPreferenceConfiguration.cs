using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Identity.Configuration;

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => new { e.UserId, e.Key }).IsUnique();

        builder.Property(e => e.Key).HasMaxLength(200);
        builder.Property(e => e.ValueJson).HasColumnType("jsonb");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            // Pin pre-move constraint name (assembly relocation shifted EF's
            // default FK-name generation — keep the live DB constraint stable).
            .HasConstraintName("fk_user_preferences__asp_net_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
