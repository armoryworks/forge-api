using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class JobOperationConfiguration : IEntityTypeConfiguration<JobOperation>
{
    public void Configure(EntityTypeBuilder<JobOperation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.CompletedQuantity).HasPrecision(10, 2);
        builder.Property(e => e.ScrapQuantity).HasPrecision(10, 2);
        builder.Property(e => e.EstSetupMinutes).HasPrecision(10, 2);
        builder.Property(e => e.EstRunMinutesEach).HasPrecision(14, 6);
        builder.Property(e => e.EstRunMinutesLot).HasPrecision(10, 2);

        builder.HasIndex(e => new { e.JobId, e.OperationId })
            .IsUnique()
            .HasFilter("(deleted_at IS NULL)")
            .HasDatabaseName("ux_job_operations_job_id_operation_id");
        builder.HasIndex(e => new { e.OperationId, e.Status });

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Operation)
            .WithMany()
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
