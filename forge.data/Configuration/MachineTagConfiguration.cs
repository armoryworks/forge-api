using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MachineTagConfiguration : IEntityTypeConfiguration<MachineTag>
{
    public void Configure(EntityTypeBuilder<MachineTag> builder)
    {
        builder.Property(e => e.TagName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.OpcNodeId).HasMaxLength(500).IsRequired();
        builder.Property(e => e.DataType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(50);
        builder.Property(e => e.WarningThresholdLow).HasPrecision(18, 4);
        builder.Property(e => e.WarningThresholdHigh).HasPrecision(18, 4);
        builder.Property(e => e.AlarmThresholdLow).HasPrecision(18, 4);
        builder.Property(e => e.AlarmThresholdHigh).HasPrecision(18, 4);

        builder.HasOne(e => e.Connection)
            .WithMany(c => c.Tags)
            .HasForeignKey(e => e.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ConnectionId);
    }
}
