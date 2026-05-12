using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ApprovalWorkflowConfiguration : IEntityTypeConfiguration<ApprovalWorkflow>
{
    public void Configure(EntityTypeBuilder<ApprovalWorkflow> builder)
    {
        builder.Property(w => w.Name).HasMaxLength(200);
        builder.Property(w => w.EntityType).HasMaxLength(50);
        builder.Property(w => w.Description).HasMaxLength(500);

        builder.HasIndex(w => w.EntityType);
        builder.HasIndex(w => w.IsActive);
    }
}
