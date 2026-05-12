using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.ProjectNumber).HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Notes).HasMaxLength(4000);
        builder.Property(e => e.BudgetTotal).HasPrecision(18, 2);
        builder.Property(e => e.ActualTotal).HasPrecision(18, 2);
        builder.Property(e => e.CommittedTotal).HasPrecision(18, 2);
        builder.Property(e => e.EstimateAtCompletionTotal).HasPrecision(18, 2);
        builder.Property(e => e.RevenueRecognized).HasPrecision(18, 2);
        builder.Property(e => e.PercentComplete).HasPrecision(5, 2);

        builder.HasIndex(e => e.ProjectNumber).IsUnique();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.SalesOrderId);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SalesOrder)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.WbsElements)
            .WithOne(e => e.Project)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
