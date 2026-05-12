using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CalibrationRecordConfiguration : IEntityTypeConfiguration<CalibrationRecord>
{
    public void Configure(EntityTypeBuilder<CalibrationRecord> builder)
    {
        builder.HasIndex(e => e.GageId);
        builder.HasIndex(e => e.CalibratedById);
        builder.HasIndex(e => e.CalibratedAt);
        builder.HasIndex(e => e.CertificateFileId);

        builder.Property(e => e.LabName).HasMaxLength(200);
        builder.Property(e => e.StandardsUsed).HasMaxLength(500);
        builder.Property(e => e.AsFoundCondition).HasMaxLength(2000);
        builder.Property(e => e.AsLeftCondition).HasMaxLength(2000);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.Gage)
            .WithMany(g => g.CalibrationRecords)
            .HasForeignKey(e => e.GageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CertificateFile)
            .WithMany()
            .HasForeignKey(e => e.CertificateFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
