using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChatMessageMentionConfiguration : IEntityTypeConfiguration<ChatMessageMention>
{
    public void Configure(EntityTypeBuilder<ChatMessageMention> builder)
    {
        builder.Property(m => m.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(m => m.DisplayText).HasMaxLength(200).IsRequired();

        builder.HasIndex(m => new { m.EntityType, m.EntityId });
        builder.HasIndex(m => m.ChatMessageId);
    }
}
