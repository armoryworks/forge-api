using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CorrectiveActionConfiguration : IEntityTypeConfiguration<CorrectiveAction>
{
    public void Configure(EntityTypeBuilder<CorrectiveAction> builder)
    {
        builder.Property(e => e.CapaNumber).HasMaxLength(25);
        builder.Property(e => e.Title).HasMaxLength(500);
        builder.Property(e => e.ProblemDescription).HasMaxLength(4000);
        builder.Property(e => e.ImpactDescription).HasMaxLength(4000);
        builder.Property(e => e.RootCauseAnalysis).HasMaxLength(4000);
        builder.Property(e => e.RootCauseMethodData).HasColumnType("jsonb");
        builder.Property(e => e.ContainmentAction).HasMaxLength(4000);
        builder.Property(e => e.CorrectiveActionDescription).HasMaxLength(4000);
        builder.Property(e => e.PreventiveAction).HasMaxLength(4000);
        builder.Property(e => e.VerificationMethod).HasMaxLength(2000);
        builder.Property(e => e.VerificationResult).HasMaxLength(4000);
        builder.Property(e => e.EffectivenessResult).HasMaxLength(4000);
        builder.Property(e => e.SourceEntityType).HasMaxLength(100);

        builder.HasIndex(e => e.CapaNumber).IsUnique();
        builder.HasIndex(e => e.OwnerId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Priority);
        builder.HasIndex(e => e.DueDate);
        builder.HasIndex(e => e.ClosedById);
        builder.HasIndex(e => e.RootCauseAnalyzedById);
        builder.HasIndex(e => e.VerifiedById);
        builder.HasIndex(e => e.EffectivenessCheckedById);
        builder.HasIndex(e => e.SourceEntityId);

        // FK-only ApplicationUser references
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.ClosedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.RootCauseAnalyzedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.VerifiedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.EffectivenessCheckedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
