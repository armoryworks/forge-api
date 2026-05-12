using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MachineConnectionConfiguration : IEntityTypeConfiguration<MachineConnection>
{
    public void Configure(EntityTypeBuilder<MachineConnection> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.OpcUaEndpoint).HasMaxLength(500).IsRequired();
        builder.Property(e => e.SecurityPolicy).HasMaxLength(100);
        builder.Property(e => e.AuthType).HasMaxLength(50);
        builder.Property(e => e.EncryptedCredentials).HasMaxLength(2000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.LastError).HasMaxLength(2000);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.IsActive);
    }
}
