using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.Property(r => r.EntityType).HasMaxLength(50);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.EntitySummary).HasMaxLength(500);

        builder.HasIndex(r => r.WorkflowId);
        builder.HasIndex(r => r.RequestedById);
        builder.HasIndex(r => new { r.EntityType, r.EntityId });
        builder.HasIndex(r => r.Status);

        builder.HasOne(r => r.Workflow)
            .WithMany(w => w.Requests)
            .HasForeignKey(r => r.WorkflowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.RequestedById);
    }
}
