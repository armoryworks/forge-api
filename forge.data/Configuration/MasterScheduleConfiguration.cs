using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MasterScheduleConfiguration : IEntityTypeConfiguration<MasterSchedule>
{
    public void Configure(EntityTypeBuilder<MasterSchedule> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedByUserId);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.MasterSchedule)
            .HasForeignKey(l => l.MasterScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
