using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ConsignmentAgreementConfiguration : IEntityTypeConfiguration<ConsignmentAgreement>
{
    public void Configure(EntityTypeBuilder<ConsignmentAgreement> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.AgreedUnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.MinStockQuantity).HasPrecision(18, 4);
        builder.Property(e => e.MaxStockQuantity).HasPrecision(18, 4);
        builder.Property(e => e.Terms).HasMaxLength(4000);

        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Transactions)
            .WithOne(t => t.Agreement)
            .HasForeignKey(t => t.AgreementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
