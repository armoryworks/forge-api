using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TrainingScanLogConfiguration : IEntityTypeConfiguration<TrainingScanLog>
{
    public void Configure(EntityTypeBuilder<TrainingScanLog> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.WasSuccessful).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.ActionType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Context.ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.UserId, e.ScannedAt });
        builder.Property(e => e.ErrorMessage).HasMaxLength(500);

        builder.HasIndex(e => e.PartId).HasFilter("part_id IS NOT NULL");
        builder.HasIndex(e => e.JobId).HasFilter("job_id IS NOT NULL");
    }
}
