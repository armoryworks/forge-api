using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SpcOocEventConfiguration : IEntityTypeConfiguration<SpcOocEvent>
{
    public void Configure(EntityTypeBuilder<SpcOocEvent> builder)
    {
        builder.Property(e => e.RuleName).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.AcknowledgmentNotes).HasMaxLength(2000);

        builder.HasIndex(e => e.CharacteristicId);
        builder.HasIndex(e => e.MeasurementId);
        builder.HasIndex(e => e.AcknowledgedById);
        builder.HasIndex(e => e.CapaId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Characteristic)
            .WithMany()
            .HasForeignKey(e => e.CharacteristicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.AcknowledgedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
