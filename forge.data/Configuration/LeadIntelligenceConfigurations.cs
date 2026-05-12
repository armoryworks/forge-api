using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class LeadSourceConfiguration : IEntityTypeConfiguration<LeadSource>
{
    public void Configure(EntityTypeBuilder<LeadSource> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Code).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique().HasFilter(@"deleted_at IS NULL");
    }
}

public class IcpRubricConfiguration : IEntityTypeConfiguration<IcpRubric>
{
    public void Configure(EntityTypeBuilder<IcpRubric> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.HasIndex(e => e.IsActive);
        // Filtered unique — at most one IsDefault=true row at a time.
        builder.HasIndex(e => e.IsDefault).IsUnique().HasFilter(@"is_default = true AND deleted_at IS NULL");
    }
}

public class IcpDimensionConfiguration : IEntityTypeConfiguration<IcpDimension>
{
    public void Configure(EntityTypeBuilder<IcpDimension> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.FieldKey).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(200);
        builder.Property(e => e.MatchSpec).HasColumnType("jsonb");
        builder.HasOne(e => e.Rubric)
            .WithMany(r => r.Dimensions)
            .HasForeignKey(e => e.IcpRubricId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssignmentRuleConfiguration : IEntityTypeConfiguration<AssignmentRule>
{
    public void Configure(EntityTypeBuilder<AssignmentRule> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Spec).HasColumnType("jsonb");
        builder.HasIndex(e => new { e.IsActive, e.Priority });
    }
}
