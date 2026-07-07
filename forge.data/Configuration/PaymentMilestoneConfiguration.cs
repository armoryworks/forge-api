using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentMilestoneConfiguration : IEntityTypeConfiguration<PaymentMilestone>
{
    public void Configure(EntityTypeBuilder<PaymentMilestone> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.DueTrigger).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Percentage).HasPrecision(7, 4);
        builder.Property(e => e.AmountLocked).HasPrecision(18, 2);
        builder.Property(e => e.PaidAmount).HasPrecision(18, 2);
        builder.HasIndex(e => e.PaymentScheduleId);
    }
}
