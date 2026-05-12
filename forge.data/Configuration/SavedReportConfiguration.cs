using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class SavedReportConfiguration : IEntityTypeConfiguration<SavedReport>
{
    public void Configure(EntityTypeBuilder<SavedReport> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.EntitySource).HasMaxLength(50);
        builder.Property(e => e.ColumnsJson).HasColumnType("jsonb");
        builder.Property(e => e.FiltersJson).HasColumnType("jsonb");
        builder.Property(e => e.GroupByField).HasMaxLength(100);
        builder.Property(e => e.SortField).HasMaxLength(100);
        builder.Property(e => e.SortDirection).HasMaxLength(10);
        builder.Property(e => e.ChartType).HasMaxLength(20);
        builder.Property(e => e.ChartLabelField).HasMaxLength(100);
        builder.Property(e => e.ChartValueField).HasMaxLength(100);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.IsShared);
    }
}
