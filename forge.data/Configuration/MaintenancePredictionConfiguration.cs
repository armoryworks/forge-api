using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MaintenancePredictionConfiguration : IEntityTypeConfiguration<MaintenancePrediction>
{
    public void Configure(EntityTypeBuilder<MaintenancePrediction> builder)
    {
        builder.Property(e => e.PredictionType).HasMaxLength(100);
        builder.Property(e => e.ModelId).HasMaxLength(100);
        builder.Property(e => e.ModelVersion).HasMaxLength(50);
        builder.Property(e => e.InputFeaturesJson).HasColumnType("jsonb");
        builder.Property(e => e.ResolutionNotes).HasMaxLength(4000);
        builder.Property(e => e.ConfidencePercent).HasPrecision(5, 2);
        builder.Property(e => e.RemainingUsefulLifeHours).HasPrecision(10, 2);

        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Severity);
        builder.HasIndex(e => e.AcknowledgedByUserId);
        builder.HasIndex(e => e.PreventiveMaintenanceJobId);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PreventiveMaintenanceJob)
            .WithMany()
            .HasForeignKey(e => e.PreventiveMaintenanceJobId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK-only ApplicationUser reference
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.AcknowledgedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
