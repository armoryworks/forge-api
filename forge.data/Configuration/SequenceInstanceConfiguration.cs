using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceInstanceConfiguration : IEntityTypeConfiguration<SequenceInstance>
{
    public void Configure(EntityTypeBuilder<SequenceInstance> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.SubjectEntityType).HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CancelReason).HasMaxLength(2000);
        // uint Version for InMemory test compat, bumped by AppDbContext.SaveChangesAsync() per IConcurrencyVersioned.
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.HasIndex(e => e.DefinitionId);
        builder.HasIndex(e => new { e.SubjectEntityType, e.SubjectEntityId });
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Definition).WithMany().HasForeignKey(e => e.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Steps).WithOne(s => s.Instance).HasForeignKey(s => s.InstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Gates).WithOne(s => s.Instance).HasForeignKey(s => s.InstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Events).WithOne(s => s.Instance).HasForeignKey(s => s.InstanceId).OnDelete(DeleteBehavior.Cascade);
    }
}
