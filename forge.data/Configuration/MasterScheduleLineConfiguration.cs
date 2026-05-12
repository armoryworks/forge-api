using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MasterScheduleLineConfiguration : IEntityTypeConfiguration<MasterScheduleLine>
{
    public void Configure(EntityTypeBuilder<MasterScheduleLine> builder)
    {
        builder.HasIndex(e => e.MasterScheduleId);
        builder.HasIndex(e => e.PartId);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
