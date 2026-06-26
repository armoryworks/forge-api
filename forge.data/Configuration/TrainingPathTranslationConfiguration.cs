using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingPathTranslationConfiguration : IEntityTypeConfiguration<TrainingPathTranslation>
{
    public void Configure(EntityTypeBuilder<TrainingPathTranslation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Locale).HasMaxLength(10);
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Description).HasMaxLength(1000);

        builder.HasIndex(e => new { e.TrainingPathId, e.Locale }).IsUnique();

        builder.HasOne(e => e.TrainingPath)
            .WithMany()
            .HasForeignKey(e => e.TrainingPathId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
