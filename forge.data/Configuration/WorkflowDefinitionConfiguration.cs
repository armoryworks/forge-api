using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.DefinitionId).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.DefinitionId).IsUnique();

        builder.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.EntityType);

        builder.Property(e => e.DefaultMode).HasMaxLength(16).IsRequired();

        builder.Property(e => e.StepsJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ExpressTemplateComponent).HasMaxLength(128);

        builder.Property(e => e.IsSeedData).IsRequired();
    }
}
