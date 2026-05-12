using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class JobStageConfiguration : IEntityTypeConfiguration<JobStage>
{
    public void Configure(EntityTypeBuilder<JobStage> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => new { e.TrackTypeId, e.SortOrder });
        builder.HasIndex(e => new { e.TrackTypeId, e.Code }).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Code).HasMaxLength(50);
        builder.Property(e => e.Color).HasMaxLength(20);

        builder.HasOne(e => e.TrackType)
            .WithMany(t => t.Stages)
            .HasForeignKey(e => e.TrackTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
