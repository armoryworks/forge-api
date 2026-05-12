using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.Property(d => d.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Comments).HasMaxLength(1000);

        builder.HasIndex(d => d.RequestId);
        builder.HasIndex(d => d.DecidedById);

        builder.HasOne(d => d.Request)
            .WithMany(r => r.Decisions)
            .HasForeignKey(d => d.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.DecidedById);
        builder.HasIndex(d => d.DelegatedToUserId);
    }
}
