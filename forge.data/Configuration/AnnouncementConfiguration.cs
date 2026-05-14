using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.Property(a => a.Title).HasMaxLength(200);
        builder.Property(a => a.Content).HasMaxLength(5000);
        builder.Property(a => a.SystemSource).HasMaxLength(50);

        builder.Property(a => a.Severity)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.Scope)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Template)
            .WithMany()
            .HasForeignKey(a => a.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.Severity, a.Scope })
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(a => a.CreatedById);
        builder.HasIndex(a => a.DepartmentId).HasFilter("department_id IS NOT NULL");
    }
}
