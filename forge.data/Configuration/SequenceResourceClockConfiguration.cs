using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceResourceClockConfiguration : IEntityTypeConfiguration<SequenceResourceClock>
{
    public void Configure(EntityTypeBuilder<SequenceResourceClock> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.ResourceType).HasMaxLength(50);
        builder.Property(e => e.ExpiryAction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EscalateRole).HasMaxLength(100);
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.HasIndex(e => new { e.ResourceType, e.ResourceId });
        builder.HasIndex(e => e.ExpiresAt).HasFilter("\"fired_at\" IS NULL AND \"deleted_at\" IS NULL");
    }
}
