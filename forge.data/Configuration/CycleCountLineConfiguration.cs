using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CycleCountLineConfiguration : IEntityTypeConfiguration<CycleCountLine>
{
    public void Configure(EntityTypeBuilder<CycleCountLine> builder)
    {
        builder.Ignore(e => e.Variance);

        builder.Property(e => e.EntityType).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasOne(e => e.CycleCount)
            .WithMany(e => e.Lines)
            .HasForeignKey(e => e.CycleCountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.BinContent)
            .WithMany()
            .HasForeignKey(e => e.BinContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.CycleCountId);
    }
}
