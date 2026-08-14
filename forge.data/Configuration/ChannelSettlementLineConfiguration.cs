using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChannelSettlementLineConfiguration : IEntityTypeConfiguration<ChannelSettlementLine>
{
    public void Configure(EntityTypeBuilder<ChannelSettlementLine> builder)
    {
        builder.Property(e => e.LineType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.ExternalOrderId).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasOne(e => e.Settlement)
            .WithMany(s => s.Lines)
            .HasForeignKey(e => e.SettlementId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not SetNull: losing the order link would silently turn a
        // reconciled proceeds line into an unattributable one.
        builder.HasOne(e => e.SalesOrder)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.SettlementId);
        builder.HasIndex(e => e.SalesOrderId);
        builder.HasIndex(e => e.LineType);
        builder.HasIndex(e => e.ExternalOrderId);
    }
}
