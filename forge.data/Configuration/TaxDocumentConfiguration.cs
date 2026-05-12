using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TaxDocumentConfiguration : IEntityTypeConfiguration<TaxDocument>
{
    public void Configure(EntityTypeBuilder<TaxDocument> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.EmployerName).HasMaxLength(200);
        builder.Property(e => e.ExternalId).HasMaxLength(100);

        builder.HasIndex(e => new { e.UserId, e.TaxYear });
        builder.HasIndex(e => e.ExternalId)
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        builder.HasOne(e => e.FileAttachment)
            .WithMany()
            .HasForeignKey(e => e.FileAttachmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
