using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SampleShipmentConfiguration : IEntityTypeConfiguration<SampleShipment>
{
    public void Configure(EntityTypeBuilder<SampleShipment> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.PartDescription).HasMaxLength(500);
        builder.Property(e => e.Status).HasMaxLength(40);
        builder.Property(e => e.TrackingNumber).HasMaxLength(100);
        builder.Property(e => e.Carrier).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.CostToUs).HasPrecision(18, 4);
        builder.Property(e => e.ChargedAmount).HasPrecision(18, 4);

        builder.HasOne(e => e.Lead)
            .WithMany()
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.LeadId);
        builder.HasIndex(e => e.Status);
    }
}
