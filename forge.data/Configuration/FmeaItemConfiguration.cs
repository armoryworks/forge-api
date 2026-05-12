using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class FmeaItemConfiguration : IEntityTypeConfiguration<FmeaItem>
{
    public void Configure(EntityTypeBuilder<FmeaItem> builder)
    {
        builder.Property(e => e.ProcessStep).HasMaxLength(500);
        builder.Property(e => e.Function).HasMaxLength(500);
        builder.Property(e => e.FailureMode).HasMaxLength(1000);
        builder.Property(e => e.PotentialEffect).HasMaxLength(1000);
        builder.Property(e => e.Classification).HasMaxLength(50);
        builder.Property(e => e.PotentialCause).HasMaxLength(1000);
        builder.Property(e => e.CurrentPreventionControls).HasMaxLength(2000);
        builder.Property(e => e.CurrentDetectionControls).HasMaxLength(2000);
        builder.Property(e => e.RecommendedAction).HasMaxLength(2000);
        builder.Property(e => e.ActionTaken).HasMaxLength(2000);

        builder.HasIndex(e => e.FmeaId);
        builder.HasIndex(e => e.ResponsibleUserId);
        builder.HasIndex(e => e.CapaId);

        builder.HasOne(e => e.Fmea)
            .WithMany(f => f.Items)
            .HasForeignKey(e => e.FmeaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Capa)
            .WithMany()
            .HasForeignKey(e => e.CapaId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK-only ApplicationUser reference
        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.ResponsibleUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
