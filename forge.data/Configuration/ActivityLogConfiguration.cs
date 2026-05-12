using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.UserId);

        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.Action).HasMaxLength(100);
        builder.Property(e => e.Description).HasColumnType("text");
        builder.Property(e => e.FieldName).HasMaxLength(200);
        builder.Property(e => e.OldValue).HasColumnType("text");
        builder.Property(e => e.NewValue).HasColumnType("text");
    }
}
