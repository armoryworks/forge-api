using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Data.Configuration;

public class LeaveBalanceAdjustmentConfiguration : IEntityTypeConfiguration<LeaveBalanceAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceAdjustment> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => new { e.UserId, e.PolicyId }, "ix_leave_balance_adjustments_user_id_policy_id");
        builder.HasIndex(e => e.PolicyId);
        builder.HasIndex(e => new { e.UserId, e.PolicyId }, "ux_leave_balance_adjustments_opening")
            .IsUnique()
            .HasFilter($"adjustment_type = {(int)LeaveAdjustmentType.Opening} AND deleted_at IS NULL");

        builder.Property(e => e.Hours).HasPrecision(10, 2);
        builder.Property(e => e.BalanceAfter).HasPrecision(10, 2);
        builder.Property(e => e.Reason).HasMaxLength(500);

        builder.HasOne(e => e.Policy)
            .WithMany()
            .HasForeignKey(e => e.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
