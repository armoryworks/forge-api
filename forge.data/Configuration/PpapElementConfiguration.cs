using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PpapElementConfiguration : IEntityTypeConfiguration<PpapElement>
{
    public void Configure(EntityTypeBuilder<PpapElement> builder)
    {
        builder.Property(e => e.ElementName).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(4000);

        builder.HasIndex(e => new { e.SubmissionId, e.ElementNumber }).IsUnique();
        builder.HasIndex(e => e.AssignedToUserId);

        builder.HasOne(e => e.Submission)
            .WithMany(s => s.Elements)
            .HasForeignKey(e => e.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK-only ApplicationUser reference
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
