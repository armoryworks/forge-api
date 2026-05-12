using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class OvertimeRuleConfiguration : IEntityTypeConfiguration<OvertimeRule>
{
    public void Configure(EntityTypeBuilder<OvertimeRule> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DailyThresholdHours).HasPrecision(8, 2);
        builder.Property(e => e.WeeklyThresholdHours).HasPrecision(8, 2);
        builder.Property(e => e.OvertimeMultiplier).HasPrecision(6, 3);
        builder.Property(e => e.DoubletimeThresholdDailyHours).HasPrecision(8, 2);
        builder.Property(e => e.DoubletimeThresholdWeeklyHours).HasPrecision(8, 2);
        builder.Property(e => e.DoubletimeMultiplier).HasPrecision(6, 3);

        builder.HasIndex(e => e.IsDefault)
            .HasFilter("is_default = true AND deleted_at IS NULL")
            .IsUnique();
    }
}
