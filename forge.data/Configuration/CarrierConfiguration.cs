using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    public void Configure(EntityTypeBuilder<Carrier> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.Code).HasMaxLength(50);
        builder.Property(e => e.Scac).HasMaxLength(10);
        builder.Property(e => e.IntegrationServiceId).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.CredentialClientId).HasMaxLength(200);
        builder.Property(e => e.CredentialSecret).HasMaxLength(1000); // encrypted blob
        builder.Property(e => e.CredentialAccountNumber).HasMaxLength(50);
        builder.Property(e => e.CredentialEnvironment).HasMaxLength(20);

        // Code is the stable lookup key when present; null codes (unlabeled custom shippers) allowed.
        builder.HasIndex(e => e.Code).IsUnique().HasFilter("code IS NOT NULL");
        builder.HasIndex(e => e.IsActive);
    }
}
