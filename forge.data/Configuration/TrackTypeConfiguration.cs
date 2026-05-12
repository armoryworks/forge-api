using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrackTypeConfiguration : IEntityTypeConfiguration<TrackType>
{
    public void Configure(EntityTypeBuilder<TrackType> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Code).HasMaxLength(50);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.CustomFieldDefinitions).HasColumnType("jsonb");
    }
}
