using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

public class IdentityDocumentConfiguration : IEntityTypeConfiguration<IdentityDocument>
{
    public void Configure(EntityTypeBuilder<IdentityDocument> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.DocumentName).HasMaxLength(200);
        builder.Property(e => e.IssuingAuthority).HasMaxLength(200);
        // DocumentNumberProtected — IPiiProtector ciphertext, variable length.
        builder.Property(e => e.DocumentNumberProtected);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.VerifiedById);

        builder.HasOne(e => e.FileAttachment)
            .WithMany()
            .HasForeignKey(e => e.FileAttachmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
