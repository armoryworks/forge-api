using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PickWaveConfiguration : IEntityTypeConfiguration<PickWave>
{
    public void Configure(EntityTypeBuilder<PickWave> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.WaveNumber).HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.WaveNumber).IsUnique();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.AssignedToId);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Wave)
            .HasForeignKey(l => l.WaveId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
