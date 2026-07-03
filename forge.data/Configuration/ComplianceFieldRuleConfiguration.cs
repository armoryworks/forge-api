using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Compliance;

namespace Forge.Data.Configuration;

public class ComplianceFieldRuleConfiguration : IEntityTypeConfiguration<ComplianceFieldRule>
{
    public void Configure(EntityTypeBuilder<ComplianceFieldRule> builder)
    {
        builder.Property(e => e.FieldKey).HasMaxLength(80);
        builder.Property(e => e.ProcessStep).HasMaxLength(80);
        builder.Property(e => e.Condition).HasMaxLength(500);

        builder.HasIndex(e => e.ComplianceProfileId).HasDatabaseName("ix_compliance_field_rules_compliance_profile_id");

        builder.HasOne(e => e.ComplianceProfile)
            .WithMany(p => p.FieldRules)
            .HasForeignKey(e => e.ComplianceProfileId)
            .HasConstraintName("fk_compliance_field_rules__compliance_profiles_compliance_profile_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
