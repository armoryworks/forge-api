using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PpapSubmissionConfiguration : IEntityTypeConfiguration<PpapSubmission>
{
    public void Configure(EntityTypeBuilder<PpapSubmission> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.SubmissionNumber).HasMaxLength(20);
        builder.Property(e => e.PartRevision).HasMaxLength(50);
        builder.Property(e => e.CustomerContactName).HasMaxLength(200);
        builder.Property(e => e.CustomerResponseNotes).HasMaxLength(4000);
        builder.Property(e => e.InternalNotes).HasMaxLength(4000);

        builder.HasIndex(e => e.SubmissionNumber).IsUnique();
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.PswSignedByUserId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK-only ApplicationUser reference
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.PswSignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
