using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TimeCorrectionLogConfiguration : IEntityTypeConfiguration<TimeCorrectionLog>
{
    public void Configure(EntityTypeBuilder<TimeCorrectionLog> builder)
    {
        builder.HasOne(e => e.TimeEntry)
            .WithMany()
            .HasForeignKey(e => e.TimeEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TimeEntryId);
        builder.HasIndex(e => e.CorrectedByUserId);

        builder.Property(e => e.Reason).HasMaxLength(500).IsRequired();
        builder.Property(e => e.OriginalCategory).HasMaxLength(100);
        builder.Property(e => e.OriginalNotes).HasMaxLength(1000);
    }
}
