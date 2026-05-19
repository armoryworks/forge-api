using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingProgressConfiguration : IEntityTypeConfiguration<TrainingProgress>
{
    public void Configure(EntityTypeBuilder<TrainingProgress> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.QuizAnswersJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.UserId, e.ModuleId }).IsUnique();
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ModuleId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Module)
            .WithMany(m => m.ProgressRecords)
            .HasForeignKey(p => p.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
