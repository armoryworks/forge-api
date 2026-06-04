using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.ToTable("acct_cost_centers");

        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(e => new { e.BookId, e.Code })
            .IsUnique()
            .HasDatabaseName("ux_acct_cost_centers_book_code");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_cost_centers_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(e => e.ParentId)
            .HasConstraintName("fk_acct_cost_centers_parent")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ParentId).HasDatabaseName("ix_acct_cost_centers_parent");
    }
}
