using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SalesOrderAcceptanceConfiguration : IEntityTypeConfiguration<SalesOrderAcceptance>
{
    public void Configure(EntityTypeBuilder<SalesOrderAcceptance> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(e => new { e.SalesOrderId, e.Status });
        builder.HasIndex(e => e.AccessToken);

        builder.HasOne(e => e.SalesOrder)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // The FK to the file is optional (verbal acceptance has no document) and nulls out if the
        // file is ever removed. RecordedByUserId / AcceptedByContactId are DB-level FKs (forge-db)
        // without EF navs — intentionally not modelled here.
        builder.HasOne(e => e.FileAttachment)
            .WithMany()
            .HasForeignKey(e => e.FileAttachmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
