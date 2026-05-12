using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DeliverableConfiguration : IEntityTypeConfiguration<Deliverable>
{
    public void Configure(EntityTypeBuilder<Deliverable> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Status).HasMaxLength(50).IsRequired().HasDefaultValue("Draft");
        builder.Property(e => e.FileAttachmentIds).HasColumnType("jsonb");
        builder.Property(e => e.CloudLinkExternalId).HasMaxLength(500);

        builder.HasIndex(e => e.JobId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(e => e.ProjectId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(e => e.CustomerId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DueDate);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Project)
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
