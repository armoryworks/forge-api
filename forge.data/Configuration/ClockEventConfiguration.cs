using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ClockEventConfiguration : IEntityTypeConfiguration<ClockEvent>
{
    public void Configure(EntityTypeBuilder<ClockEvent> builder)
    {
        builder.Property(c => c.EventTypeCode).HasMaxLength(50);
        builder.Property(c => c.Reason).HasMaxLength(100);
        builder.Property(c => c.ScanMethod).HasMaxLength(50);
        builder.Property(c => c.Source).HasMaxLength(50);

        builder.HasIndex(c => new { c.UserId, c.Timestamp });
        builder.HasIndex(c => c.EventTypeCode);
        builder.HasIndex(c => c.OperationId);

        builder.HasOne<Operation>()
            .WithMany()
            .HasForeignKey(c => c.OperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
