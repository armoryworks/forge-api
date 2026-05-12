using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MlModelConfiguration : IEntityTypeConfiguration<MlModel>
{
    public void Configure(EntityTypeBuilder<MlModel> builder)
    {
        builder.Property(e => e.ModelId).HasMaxLength(100);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.ModelType).HasMaxLength(50);
        builder.Property(e => e.Version).HasMaxLength(50);
        builder.Property(e => e.PredictionType).HasMaxLength(100);
        builder.Property(e => e.ModelArtifactPath).HasMaxLength(500);
        builder.Property(e => e.HyperparametersJson).HasColumnType("jsonb");
        builder.Property(e => e.FeatureListJson).HasColumnType("jsonb");
        builder.Property(e => e.Accuracy).HasPrecision(5, 4);
        builder.Property(e => e.Precision).HasPrecision(5, 4);
        builder.Property(e => e.Recall).HasPrecision(5, 4);
        builder.Property(e => e.F1Score).HasPrecision(5, 4);

        builder.HasIndex(e => e.ModelId).IsUnique();
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
