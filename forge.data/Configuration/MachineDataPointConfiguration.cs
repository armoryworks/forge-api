using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MachineDataPointConfiguration : IEntityTypeConfiguration<MachineDataPoint>
{
    public void Configure(EntityTypeBuilder<MachineDataPoint> builder)
    {
        builder.Property(e => e.Value).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Quality).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TagId);
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => new { e.TagId, e.Timestamp });
    }
}
