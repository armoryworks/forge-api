using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Category).HasMaxLength(100);
        builder.Property(e => e.Classification).HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Vendor).HasMaxLength(200);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.NextOccurrenceDate);
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.Classification);
    }
}
