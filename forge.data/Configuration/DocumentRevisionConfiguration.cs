using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DocumentRevisionConfiguration : IEntityTypeConfiguration<DocumentRevision>
{
    public void Configure(EntityTypeBuilder<DocumentRevision> builder)
    {
        builder.Property(e => e.ChangeDescription).HasMaxLength(2000).IsRequired();

        builder.HasIndex(e => e.DocumentId);
        builder.HasIndex(e => e.FileAttachmentId);
        builder.HasIndex(e => e.AuthoredById);
        builder.HasIndex(e => new { e.DocumentId, e.RevisionNumber }).IsUnique();

        builder.HasOne(e => e.Document)
            .WithMany(d => d.Revisions)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.FileAttachment)
            .WithMany()
            .HasForeignKey(e => e.FileAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
