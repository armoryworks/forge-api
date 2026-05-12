using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DomainEventFailureConfiguration : IEntityTypeConfiguration<DomainEventFailure>
{
    public void Configure(EntityTypeBuilder<DomainEventFailure> builder)
    {
        builder.Property(e => e.EventType).HasMaxLength(200);
        builder.Property(e => e.HandlerName).HasMaxLength(200);
        builder.Property(e => e.ErrorMessage).HasMaxLength(4000);

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.FailedAt);
        builder.HasIndex(e => e.EventType);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
    }
}
