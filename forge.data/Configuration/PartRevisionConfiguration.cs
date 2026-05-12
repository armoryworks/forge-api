using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PartRevisionConfiguration : IEntityTypeConfiguration<PartRevision>
{
    public void Configure(EntityTypeBuilder<PartRevision> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Revision).HasMaxLength(20);
        builder.Property(e => e.ChangeDescription).HasMaxLength(500);
        builder.Property(e => e.ChangeReason).HasMaxLength(500);

        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => new { e.PartId, e.Revision }).IsUnique();

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
