using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class JobOperationEventConfiguration : IEntityTypeConfiguration<JobOperationEvent>
{
    public void Configure(EntityTypeBuilder<JobOperationEvent> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Quantity).HasPrecision(10, 2);
        builder.Property(e => e.ReasonCode).HasMaxLength(50);

        builder.HasIndex(e => new { e.JobOperationId, e.OccurredAt });

        builder.HasOne(e => e.JobOperation)
            .WithMany()
            .HasForeignKey(e => e.JobOperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
