using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MaintenanceLogConfiguration : IEntityTypeConfiguration<MaintenanceLog>
{
    public void Configure(EntityTypeBuilder<MaintenanceLog> builder)
    {
        builder.Property(e => e.HoursAtService).HasPrecision(18, 2);
        builder.Property(e => e.Cost).HasPrecision(18, 2);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.Schedule)
            .WithMany(e => e.Logs)
            .HasForeignKey(e => e.MaintenanceScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.MaintenanceScheduleId);
    }
}
