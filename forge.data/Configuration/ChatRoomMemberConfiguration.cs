using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChatRoomMemberConfiguration : IEntityTypeConfiguration<ChatRoomMember>
{
    public void Configure(EntityTypeBuilder<ChatRoomMember> builder)
    {
        builder.Property(m => m.Role)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(m => m.ChatRoom)
            .WithMany(r => r.Members)
            .HasForeignKey(m => m.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.LastReadMessage)
            .WithMany()
            .HasForeignKey(m => m.LastReadMessageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.ChatRoomId);
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => new { m.ChatRoomId, m.UserId }).IsUnique();
    }
}
