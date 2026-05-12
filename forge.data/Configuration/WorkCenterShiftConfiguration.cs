using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WorkCenterShiftConfiguration : IEntityTypeConfiguration<WorkCenterShift>
{
    public void Configure(EntityTypeBuilder<WorkCenterShift> builder)
    {
        builder.HasIndex(e => new { e.WorkCenterId, e.ShiftId }).IsUnique();

        builder.HasOne(e => e.WorkCenter)
            .WithMany(w => w.Shifts)
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Shift)
            .WithMany(s => s.WorkCenterShifts)
            .HasForeignKey(e => e.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
