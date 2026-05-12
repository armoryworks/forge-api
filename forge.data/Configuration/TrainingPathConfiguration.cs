using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingPathConfiguration : IEntityTypeConfiguration<TrainingPath>
{
    public void Configure(EntityTypeBuilder<TrainingPath> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Slug).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Icon).HasMaxLength(100);
        builder.Property(e => e.AllowedRoles).HasColumnType("jsonb");

        builder.HasIndex(e => e.Slug).IsUnique();

        builder.HasMany(p => p.PathModules)
            .WithOne(pm => pm.Path)
            .HasForeignKey(pm => pm.PathId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Enrollments)
            .WithOne(e => e.Path)
            .HasForeignKey(e => e.PathId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
