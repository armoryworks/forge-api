using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class LeaveBalanceConfiguration : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> builder)
    {
        builder.HasIndex(e => new { e.UserId, e.PolicyId }).IsUnique();
        builder.HasIndex(e => e.UserId);

        builder.Property(e => e.Balance).HasPrecision(10, 2);
        builder.Property(e => e.UsedThisYear).HasPrecision(10, 2);
        builder.Property(e => e.AccruedThisYear).HasPrecision(10, 2);

        builder.HasOne(e => e.Policy)
            .WithMany(p => p.Balances)
            .HasForeignKey(e => e.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
