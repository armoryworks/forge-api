using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MrpRunConfiguration : IEntityTypeConfiguration<MrpRun>
{
    public void Configure(EntityTypeBuilder<MrpRun> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.RunNumber).IsUnique();
        builder.HasIndex(e => e.Status);

        builder.Property(e => e.RunNumber).HasMaxLength(20);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(e => e.InitiatedByUserId);

        builder.HasMany(e => e.Demands)
            .WithOne(d => d.MrpRun)
            .HasForeignKey(d => d.MrpRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Supplies)
            .WithOne(s => s.MrpRun)
            .HasForeignKey(s => s.MrpRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.PlannedOrders)
            .WithOne(p => p.MrpRun)
            .HasForeignKey(p => p.MrpRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Exceptions)
            .WithOne(x => x.MrpRun)
            .HasForeignKey(x => x.MrpRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
