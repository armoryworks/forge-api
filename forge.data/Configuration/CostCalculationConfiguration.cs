using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CostCalculationConfiguration : IEntityTypeConfiguration<CostCalculation>
{
    public void Configure(EntityTypeBuilder<CostCalculation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.EntityType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntityId).IsRequired();

        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.IsCurrent);

        builder.Property(e => e.ProfileId).IsRequired();
        builder.Property(e => e.ProfileVersion).IsRequired();

        builder.HasOne(e => e.Profile)
            .WithMany()
            .HasForeignKey(e => e.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ResultAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(e => e.ResultBreakdown).HasColumnType("jsonb");

        builder.Property(e => e.CalculatedAt).IsRequired();
        builder.Property(e => e.CalculatedBy);

        builder.HasOne(e => e.Inputs)
            .WithOne(i => i.CostCalculation)
            .HasForeignKey<CostCalculationInputs>(i => i.CostCalculationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
