using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingModuleTranslationConfiguration : IEntityTypeConfiguration<TrainingModuleTranslation>
{
    public void Configure(EntityTypeBuilder<TrainingModuleTranslation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Locale).HasMaxLength(10);
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Summary).HasMaxLength(1000);
        builder.Property(e => e.ContentJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.TrainingModuleId, e.Locale }).IsUnique();

        builder.HasOne(e => e.TrainingModule)
            .WithMany()
            .HasForeignKey(e => e.TrainingModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
