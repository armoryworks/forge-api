using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Type).HasMaxLength(50);
        builder.Property(e => e.Severity).HasMaxLength(20);
        builder.Property(e => e.Source).HasMaxLength(20);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Message).HasMaxLength(2000);
        builder.Property(e => e.EntityType).HasMaxLength(50);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.SenderId);
        builder.HasIndex(e => new { e.UserId, e.IsDismissed, e.CreatedAt });
    }
}
