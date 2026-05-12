using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PayStubConfiguration : IEntityTypeConfiguration<PayStub>
{
    public void Configure(EntityTypeBuilder<PayStub> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.GrossPay).HasPrecision(18, 2);
        builder.Property(e => e.NetPay).HasPrecision(18, 2);
        builder.Property(e => e.TotalDeductions).HasPrecision(18, 2);
        builder.Property(e => e.TotalTaxes).HasPrecision(18, 2);
        builder.Property(e => e.ExternalId).HasMaxLength(100);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ExternalId)
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        builder.HasOne(e => e.FileAttachment)
            .WithMany()
            .HasForeignKey(e => e.FileAttachmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
