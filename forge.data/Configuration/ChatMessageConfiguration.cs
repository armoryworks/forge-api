using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.ThreadReplyCount).HasDefaultValueSql("0");
        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.SenderId);
        builder.HasIndex(m => m.RecipientId);
        builder.HasIndex(m => new { m.SenderId, m.RecipientId, m.CreatedAt });

        // Thread self-FK
        builder.HasOne(m => m.ParentMessage)
            .WithMany()
            .HasForeignKey(m => m.ParentMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.ParentMessageId)
            .HasFilter("parent_message_id IS NOT NULL");

        // Mentions navigation
        builder.HasMany(m => m.Mentions)
            .WithOne(mm => mm.ChatMessage)
            .HasForeignKey(mm => mm.ChatMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
