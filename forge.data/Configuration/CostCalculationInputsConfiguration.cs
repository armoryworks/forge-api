using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CostCalculationInputsConfiguration : IEntityTypeConfiguration<CostCalculationInputs>
{
    public void Configure(EntityTypeBuilder<CostCalculationInputs> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.CostCalculationId).IsUnique();

        builder.Property(e => e.DirectMaterialCost).HasColumnType("decimal(18,4)");
        builder.Property(e => e.DirectLaborHours).HasColumnType("decimal(10,2)");
        builder.Property(e => e.DirectLaborCost).HasColumnType("decimal(18,4)");
        builder.Property(e => e.MachineHours).HasColumnType("decimal(10,2)");
        builder.Property(e => e.OverheadAmount).HasColumnType("decimal(18,4)");
        builder.Property(e => e.OverheadRatePct).HasColumnType("decimal(7,4)");

        builder.Property(e => e.CustomInputs).HasColumnType("jsonb");
    }
}
