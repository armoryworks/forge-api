using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PerformanceReviewConfiguration : IEntityTypeConfiguration<PerformanceReview>
{
    public void Configure(EntityTypeBuilder<PerformanceReview> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.CycleId);
        builder.HasIndex(e => e.EmployeeId);
        builder.HasIndex(e => e.ReviewerId);
        builder.HasIndex(e => new { e.CycleId, e.EmployeeId }).IsUnique();

        builder.Property(e => e.OverallRating).HasPrecision(3, 1);
        builder.Property(e => e.StrengthsComments).HasMaxLength(5000);
        builder.Property(e => e.ImprovementComments).HasMaxLength(5000);
        builder.Property(e => e.EmployeeSelfAssessment).HasMaxLength(5000);

        builder.HasOne(e => e.Cycle)
            .WithMany(c => c.Reviews)
            .HasForeignKey(e => e.CycleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
