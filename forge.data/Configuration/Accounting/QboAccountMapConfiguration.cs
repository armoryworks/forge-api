using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class QboAccountMapConfiguration : IEntityTypeConfiguration<QboAccountMap>
{
    public void Configure(EntityTypeBuilder<QboAccountMap> builder)
    {
        builder.ToTable("acct_qbo_account_maps");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.QboAccountId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.QboAccountName).HasMaxLength(200);

        // One live mapping per GL account; the filter lets a deleted mapping be re-created.
        builder.HasIndex(e => e.GlAccountId)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_acct_qbo_account_maps_gl_account");

        builder.HasOne(e => e.GlAccount)
            .WithMany()
            .HasForeignKey(e => e.GlAccountId)
            .HasConstraintName("fk_acct_qbo_account_maps_gl_account")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
