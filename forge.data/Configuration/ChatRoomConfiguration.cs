using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.ChannelType).HasDefaultValueSql("''");
        builder.Property(e => e.CreatedBySystem).HasDefaultValueSql("false");
        builder.Property(e => e.IsReadOnly).HasDefaultValueSql("false");
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.IconName).HasMaxLength(50);

        builder.Property(r => r.ChannelType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Team)
            .WithMany()
            .HasForeignKey(r => r.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => r.CreatedById);
        builder.HasIndex(r => r.TeamId).HasFilter("team_id IS NOT NULL");
        builder.HasIndex(r => r.ChannelType);
    }
}
