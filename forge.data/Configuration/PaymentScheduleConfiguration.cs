using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentScheduleConfiguration : IEntityTypeConfiguration<PaymentSchedule>
{
    public void Configure(EntityTypeBuilder<PaymentSchedule> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        // One live schedule per document (partial unique indexes in forge-db).
        builder.HasIndex(e => e.QuoteId);
        builder.HasIndex(e => e.SalesOrderId);
    }
}
