using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SerialHistoryConfiguration : IEntityTypeConfiguration<SerialHistory>
{
    public void Configure(EntityTypeBuilder<SerialHistory> builder)
    {
        builder.HasIndex(e => e.SerialNumberId);
        builder.HasIndex(e => e.ActorId);
        builder.HasIndex(e => e.OccurredAt);

        builder.Property(e => e.Action).HasMaxLength(50);
        builder.Property(e => e.FromLocationName).HasMaxLength(200);
        builder.Property(e => e.ToLocationName).HasMaxLength(200);
        builder.Property(e => e.Details).HasMaxLength(2000);

        builder.HasOne(e => e.SerialNumber)
            .WithMany(s => s.History)
            .HasForeignKey(e => e.SerialNumberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
