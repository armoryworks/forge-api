using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.PolicyId);
        builder.HasIndex(e => e.ApprovedById);
        builder.HasIndex(e => e.Status);

        builder.Property(e => e.Hours).HasPrecision(10, 2);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.DenialReason).HasMaxLength(1000);

        builder.HasOne(e => e.Policy)
            .WithMany(p => p.Requests)
            .HasForeignKey(e => e.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
