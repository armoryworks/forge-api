using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PredictionFeedbackConfiguration : IEntityTypeConfiguration<PredictionFeedback>
{
    public void Configure(EntityTypeBuilder<PredictionFeedback> builder)
    {
        builder.Property(e => e.Notes).HasMaxLength(4000);
        builder.Property(e => e.PredictionErrorHours).HasPrecision(10, 2);

        builder.HasIndex(e => e.PredictionId);
        builder.HasIndex(e => e.RecordedByUserId);

        builder.HasOne(e => e.Prediction)
            .WithMany()
            .HasForeignKey(e => e.PredictionId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK-only ApplicationUser reference
        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
