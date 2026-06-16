using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.IsOnCreditHold).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsTaxExempt).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsAerospace).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsAutomotive).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsFdaRegulated).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsItarControlled).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsReferenceOk).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Ignore(e => e.IsDeleted);
        // Phase 3 H2 / WU-12: IActiveAware contract member — pure compute.
        builder.Ignore(e => e.IsActiveForNewTransactions);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.CompanyName).HasMaxLength(200);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        // Credit management
        builder.Property(e => e.CreditLimit).HasPrecision(18, 2);
        builder.Property(e => e.CreditHoldReason).HasMaxLength(500);
        builder.HasIndex(e => e.CreditHoldById);

        // Tax exemption — state cert # max ~50 chars (CA ~12, NY ~9, TX 11).
        // Indexed because audit reports commonly filter "exempt customers only".
        builder.Property(e => e.TaxExemptionId).HasMaxLength(50);
        builder.HasIndex(e => e.IsTaxExempt);

        // Phase 3 F3 — default tax/currency captured at create-time.
        builder.Property(e => e.DefaultCurrency).HasMaxLength(3);
        builder.HasOne(e => e.DefaultTaxCode)
            .WithMany()
            .HasForeignKey(e => e.DefaultTaxCodeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.DefaultTaxCodeId);

        // SourceLead inverse nav is configured from the Lead side
        // (LeadConfiguration's HasOne(ConvertedCustomer).WithOne(SourceLead)).
        // No additional Customer-side wiring required — EF Core resolves
        // the back-link from the FK on Lead.ConvertedCustomerId.
    }
}
