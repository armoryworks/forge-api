using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WbsElementConfiguration : IEntityTypeConfiguration<WbsElement>
{
    public void Configure(EntityTypeBuilder<WbsElement> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Code).HasMaxLength(50);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.BudgetLabor).HasPrecision(18, 2);
        builder.Property(e => e.BudgetMaterial).HasPrecision(18, 2);
        builder.Property(e => e.BudgetOther).HasPrecision(18, 2);
        builder.Property(e => e.BudgetTotal).HasPrecision(18, 2);
        builder.Property(e => e.ActualLabor).HasPrecision(18, 2);
        builder.Property(e => e.ActualMaterial).HasPrecision(18, 2);
        builder.Property(e => e.ActualOther).HasPrecision(18, 2);
        builder.Property(e => e.ActualTotal).HasPrecision(18, 2);
        builder.Property(e => e.PercentComplete).HasPrecision(5, 2);

        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.ParentElementId);
        builder.HasIndex(e => new { e.ProjectId, e.Code }).IsUnique();

        builder.HasOne(e => e.ParentElement)
            .WithMany(e => e.ChildElements)
            .HasForeignKey(e => e.ParentElementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.CostEntries)
            .WithOne(e => e.WbsElement)
            .HasForeignKey(e => e.WbsElementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
