using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EdiTransactionConfiguration : IEntityTypeConfiguration<EdiTransaction>
{
    public void Configure(EntityTypeBuilder<EdiTransaction> builder)
    {
        builder.Property(e => e.TransactionSet).HasMaxLength(10).IsRequired();
        builder.Property(e => e.ControlNumber).HasMaxLength(50);
        builder.Property(e => e.GroupControlNumber).HasMaxLength(50);
        builder.Property(e => e.TransactionControlNumber).HasMaxLength(50);
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.RelatedEntityType).HasMaxLength(100);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.ParsedDataJson).HasColumnType("jsonb");
        builder.Property(e => e.ErrorDetailJson).HasColumnType("jsonb");

        builder.HasOne(e => e.TradingPartner)
            .WithMany(p => p.Transactions)
            .HasForeignKey(e => e.TradingPartnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.AcknowledgmentTransaction)
            .WithMany()
            .HasForeignKey(e => e.AcknowledgmentTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.TradingPartnerId);
        builder.HasIndex(e => e.Direction);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.TransactionSet);
        builder.HasIndex(e => e.ReceivedAt);
        builder.HasIndex(e => new { e.RelatedEntityType, e.RelatedEntityId });
        builder.HasIndex(e => e.AcknowledgmentTransactionId);
    }
}
