using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ReferenceDataConfiguration : IEntityTypeConfiguration<ReferenceData>
{
    public void Configure(EntityTypeBuilder<ReferenceData> builder)
    {
        builder.HasIndex(e => new { e.GroupCode, e.Code }).IsUnique();

        builder.Property(e => e.GroupCode).HasMaxLength(50);
        builder.Property(e => e.Code).HasMaxLength(50);
        builder.Property(e => e.Label).HasMaxLength(200);
        builder.Property(e => e.Metadata).HasColumnType("jsonb");
        builder.Property(e => e.IsSeedData).HasDefaultValue(false);

        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
