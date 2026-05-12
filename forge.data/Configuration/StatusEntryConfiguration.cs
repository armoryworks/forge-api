using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class StatusEntryConfiguration : IEntityTypeConfiguration<StatusEntry>
{
    public void Configure(EntityTypeBuilder<StatusEntry> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(s => s.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(s => s.StatusCode).IsRequired().HasMaxLength(50);
        builder.Property(s => s.StatusLabel).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Category).IsRequired().HasMaxLength(20);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.HasIndex(s => new { s.EntityType, s.EntityId });
        builder.HasIndex(s => new { s.EntityType, s.EntityId, s.Category });
        builder.HasIndex(s => new { s.EntityType, s.EntityId, s.EndedAt });
        builder.HasIndex(s => s.SetById);
        builder.HasIndex(s => s.WorkCenterId);
        builder.HasIndex(s => s.OperationId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.SetById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.WorkCenter)
            .WithMany()
            .HasForeignKey(s => s.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Operation)
            .WithMany()
            .HasForeignKey(s => s.OperationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
