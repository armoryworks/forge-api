using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CapaTaskConfiguration : IEntityTypeConfiguration<CapaTask>
{
    public void Configure(EntityTypeBuilder<CapaTask> builder)
    {
        builder.Property(e => e.Title).HasMaxLength(500);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.CompletionNotes).HasMaxLength(4000);

        builder.HasIndex(e => e.CapaId);
        builder.HasIndex(e => e.AssigneeId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Capa)
            .WithMany(c => c.Tasks)
            .HasForeignKey(e => e.CapaId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK-only ApplicationUser references
        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Forge.Data.Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.CompletedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
