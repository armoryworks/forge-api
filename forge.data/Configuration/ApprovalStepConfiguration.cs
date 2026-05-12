using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.ApproverType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ApproverRole).HasMaxLength(50);
        builder.Property(s => s.AutoApproveBelow).HasPrecision(18, 2);

        builder.HasIndex(s => s.WorkflowId);

        builder.HasOne(s => s.Workflow)
            .WithMany(w => w.Steps)
            .HasForeignKey(s => s.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.ApproverUserId);
    }
}
