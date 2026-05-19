using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Data.Configuration;

public class FollowUpTaskConfiguration : IEntityTypeConfiguration<FollowUpTask>
{
    public void Configure(EntityTypeBuilder<FollowUpTask> builder)
    {
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.SourceEntityType).HasMaxLength(100);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.AssignedToUserId);
        builder.HasIndex(e => e.Status)
            .HasFilter("status = 'Open'");
        builder.HasIndex(e => e.DueDate);
        builder.HasIndex(e => new { e.SourceEntityType, e.SourceEntityId });
        builder.HasIndex(e => e.TriggerType);

        builder.Property(e => e.TriggerType).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
    }
}
