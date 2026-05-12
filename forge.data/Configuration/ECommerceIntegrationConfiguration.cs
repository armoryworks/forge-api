using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ECommerceIntegrationConfiguration : IEntityTypeConfiguration<ECommerceIntegration>
{
    public void Configure(EntityTypeBuilder<ECommerceIntegration> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Platform).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.EncryptedCredentials).HasMaxLength(4000).IsRequired();
        builder.Property(e => e.StoreUrl).HasMaxLength(500);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.PartMappingsJson).HasColumnType("jsonb");

        builder.HasOne(e => e.DefaultCustomer)
            .WithMany()
            .HasForeignKey(e => e.DefaultCustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.Platform);
        builder.HasIndex(e => e.DefaultCustomerId);
    }
}
