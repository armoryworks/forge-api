using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(f => f.FileName).HasMaxLength(500);
        builder.Property(f => f.ContentType).HasMaxLength(200);
        builder.Property(f => f.BucketName).HasMaxLength(100);
        builder.Property(f => f.ObjectKey).HasMaxLength(500);
        builder.Property(f => f.EntityType).HasMaxLength(50);
        builder.Property(f => f.RequiredRole).HasMaxLength(100);

        builder.HasIndex(f => new { f.EntityType, f.EntityId });
        builder.HasIndex(f => f.UploadedById);
        builder.HasIndex(f => f.PartRevisionId);

        builder.HasOne(f => f.PartRevision)
            .WithMany(r => r.Files)
            .HasForeignKey(f => f.PartRevisionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
