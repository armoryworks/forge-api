using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ControlledDocumentConfiguration : IEntityTypeConfiguration<ControlledDocument>
{
    public void Configure(EntityTypeBuilder<ControlledDocument> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Category).HasMaxLength(50).IsRequired();

        builder.HasIndex(e => e.DocumentNumber).IsUnique();
        builder.HasIndex(e => e.OwnerId);
        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Revisions)
            .WithOne(r => r.Document)
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
