using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ BANKING BOUNDARY — VendorBankAccount. The encrypted columns hold Data-Protection
/// ciphertext (sizes allow the envelope overhead); the masked columns are the only
/// display representation. Status stored as string for greppable ops queries (the
/// CIT incident lesson — accounting/banking status columns read better as text).
/// </summary>
public class VendorBankAccountConfiguration : IEntityTypeConfiguration<VendorBankAccount>
{
    public void Configure(EntityTypeBuilder<VendorBankAccount> builder)
    {
        builder.ToTable("vendor_bank_accounts");

        builder.Property(e => e.Nickname).HasMaxLength(100).IsRequired();
        builder.Property(e => e.RoutingNumberEncrypted).HasMaxLength(500).IsRequired();
        builder.Property(e => e.AccountNumberEncrypted).HasMaxLength(500).IsRequired();
        builder.Property(e => e.RoutingNumberMasked).HasMaxLength(20).IsRequired();
        builder.Property(e => e.AccountNumberMasked).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.VendorId).HasDatabaseName("ix_vendor_bank_accounts_vendor");
        builder.HasIndex(e => e.Status).HasDatabaseName("ix_vendor_bank_accounts_status");

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .HasConstraintName("fk_vendor_bank_accounts_vendor")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
