using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CommunicationArtifactConfiguration : IEntityTypeConfiguration<CommunicationArtifact>
{
    public void Configure(EntityTypeBuilder<CommunicationArtifact> builder)
    {
        builder.Ignore(e => e.DisplayName);
        builder.Ignore(e => e.ShortHash);

        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        // char(64), not varchar: a SHA-256 hex digest is exactly 64 characters,
        // and fixing the width makes a truncated or malformed value fail loudly
        // at insert rather than silently becoming a hash that matches nothing.
        builder.Property(e => e.Sha256).HasColumnType("character(64)").IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(255).IsRequired();
        builder.Property(e => e.OriginalFilename).HasMaxLength(500);
        builder.Property(e => e.BucketName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ObjectKey).HasMaxLength(500).IsRequired();

        // Cascade: artifacts have no meaning without their communication. The
        // immutability trigger blocks the DELETE anyway, so this is the declared
        // intent rather than a live path.
        builder.HasOne(e => e.Communication)
            .WithMany(c => c.Artifacts)
            .HasForeignKey(e => e.CommunicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.CommunicationId);

        // Content-addressed lookup: "have we already stored these exact bytes",
        // and the reverse lookup from a quoted hash back to its message.
        builder.HasIndex(e => e.Sha256);

        // Exactly one raw message per communication. Attachments are unbounded.
        builder.HasIndex(e => e.CommunicationId)
            .IsUnique()
            .HasFilter("kind = 'Message'")
            .HasDatabaseName("ux_communication_artifacts_message");
    }
}
