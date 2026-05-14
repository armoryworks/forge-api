using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingModuleConfiguration : IEntityTypeConfiguration<TrainingModule>
{
    public void Configure(EntityTypeBuilder<TrainingModule> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Slug).HasMaxLength(200);
        builder.Property(e => e.Summary).HasMaxLength(1000);
        builder.Property(e => e.ContentJson).HasColumnType("jsonb");
        builder.Property(e => e.Tags).HasColumnType("jsonb");
        builder.Property(e => e.AppRoutes).HasColumnType("jsonb");
        builder.Property(e => e.CoverImageUrl).HasMaxLength(500);

        builder.HasIndex(e => e.Slug).IsUnique();
        builder.HasIndex(e => e.IsPublished);
        builder.HasIndex(e => e.IsOnboardingRequired);
        builder.HasIndex(e => e.CreatedByUserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
