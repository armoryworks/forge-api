using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EngineeringChangeOrderConfiguration : IEntityTypeConfiguration<EngineeringChangeOrder>
{
    public void Configure(EntityTypeBuilder<EngineeringChangeOrder> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.EcoNumber).IsUnique();

        builder.Property(e => e.EcoNumber).HasMaxLength(30);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.ReasonForChange).HasMaxLength(2000);
        builder.Property(e => e.ImpactAnalysis).HasMaxLength(2000);

        builder.HasIndex(e => e.RequestedById);
        builder.HasIndex(e => e.ApprovedById);
        builder.HasIndex(e => e.Status);
    }
}
