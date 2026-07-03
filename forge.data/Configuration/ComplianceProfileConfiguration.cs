using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Compliance;

namespace Forge.Data.Configuration;

public class ComplianceProfileConfiguration : IEntityTypeConfiguration<ComplianceProfile>
{
    public void Configure(EntityTypeBuilder<ComplianceProfile> builder)
    {
        builder.Property(e => e.IndustryKey).HasMaxLength(60);
        builder.Property(e => e.Name).HasMaxLength(120);
        builder.Property(e => e.RequiredTraceabilityType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.IndustryKey).IsUnique().HasDatabaseName("ix_compliance_profiles_industry_key");
    }
}
