using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MfaRecoveryCodeConfiguration : IEntityTypeConfiguration<MfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<MfaRecoveryCode> builder)
    {
        builder.Property(c => c.CodeHash).HasMaxLength(256);
        builder.Property(c => c.UsedFromIp).HasMaxLength(50);

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => new { c.UserId, c.IsUsed });
    }
}
