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
            .OnDelete(DeleteBehavior.Cascade);
    }
}
