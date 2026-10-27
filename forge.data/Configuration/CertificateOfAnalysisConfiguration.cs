using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CertificateOfAnalysisConfiguration : IEntityTypeConfiguration<CertificateOfAnalysis>
{
    public void Configure(EntityTypeBuilder<CertificateOfAnalysis> builder)
    {
        builder.ToTable("certificates_of_analysis");
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.CertificateNumber).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Language).HasMaxLength(10).HasDefaultValueSql("'en'::character varying").ValueGeneratedNever();
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Conforms).HasDefaultValueSql("true").ValueGeneratedNever();

        builder.HasIndex(e => e.CertificateNumber).IsUnique();

        builder.HasOne(e => e.LotRecord).WithMany().HasForeignKey(e => e.LotRecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Part).WithMany().HasForeignKey(e => e.PartId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.Shipment).WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.FileAttachment).WithMany().HasForeignKey(e => e.FileAttachmentId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Results)
            .WithOne(r => r.Certificate)
            .HasForeignKey(r => r.CertificateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
