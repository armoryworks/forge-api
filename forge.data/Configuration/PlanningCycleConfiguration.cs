using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PlanningCycleConfiguration : IEntityTypeConfiguration<PlanningCycle>
{
    public void Configure(EntityTypeBuilder<PlanningCycle> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Goals).HasMaxLength(2000);

        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Entries)
            .WithOne(e => e.PlanningCycle)
            .HasForeignKey(e => e.PlanningCycleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
