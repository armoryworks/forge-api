using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ChannelSettlementConfiguration : IEntityTypeConfiguration<ChannelSettlement>
{
    public void Configure(EntityTypeBuilder<ChannelSettlement> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.ComputedNetAmount);
        builder.Ignore(e => e.Variance);

        builder.Property(e => e.ExternalSettlementId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ReportedNetAmount).HasPrecision(18, 2);
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ResolutionNotes).HasMaxLength(2000);
        builder.Property(e => e.RawPayloadJson).HasColumnType("jsonb");

        builder.HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotency key for re-import — a settlement replay must update the
        // existing batch, never mint a duplicate payout.
        builder.HasIndex(e => new { e.ChannelId, e.ExternalSettlementId }).IsUnique();

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DepositedAt);
    }
}
