using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EdiTradingPartnerConfiguration : IEntityTypeConfiguration<EdiTradingPartner>
{
    public void Configure(EntityTypeBuilder<EdiTradingPartner> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.QualifierId).HasMaxLength(10).IsRequired();
        builder.Property(e => e.QualifierValue).HasMaxLength(100).IsRequired();
        builder.Property(e => e.InterchangeSenderId).HasMaxLength(50);
        builder.Property(e => e.InterchangeReceiverId).HasMaxLength(50);
        builder.Property(e => e.ApplicationSenderId).HasMaxLength(50);
        builder.Property(e => e.ApplicationReceiverId).HasMaxLength(50);
        builder.Property(e => e.DefaultFormat).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TransportMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TransportConfigJson).HasColumnType("jsonb");
        builder.Property(e => e.DefaultMappingProfileId).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => new { e.QualifierId, e.QualifierValue });
    }
}
