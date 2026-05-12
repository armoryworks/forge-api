using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DiscoveryRunConfiguration : IEntityTypeConfiguration<DiscoveryRun>
{
    public void Configure(EntityTypeBuilder<DiscoveryRun> builder)
    {
        builder.Property(e => e.RunByUserId).IsRequired();
        builder.HasIndex(e => e.RunByUserId);

        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.CompletedAt).IsRequired();
        builder.HasIndex(e => e.CompletedAt);

        builder.Property(e => e.RecommendedPresetId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.AppliedPresetId).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.AppliedPresetId);

        builder.Property(e => e.RecommendedConfidence);

        builder.Property(e => e.AnswersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.AppliedDeltasJson).HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.RanInConsultantMode).IsRequired();
    }
}
