using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EdiMappingConfiguration : IEntityTypeConfiguration<EdiMapping>
{
    public void Configure(EntityTypeBuilder<EdiMapping> builder)
    {
        builder.Property(e => e.TransactionSet).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.FieldMappingsJson).HasColumnType("jsonb");
        builder.Property(e => e.ValueTranslationsJson).HasColumnType("jsonb");
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.TradingPartner)
            .WithMany(p => p.Mappings)
            .HasForeignKey(e => e.TradingPartnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TradingPartnerId);
        builder.HasIndex(e => new { e.TradingPartnerId, e.TransactionSet });
    }
}
