using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Subtotal);
        builder.Ignore(e => e.TaxAmount);
        builder.Ignore(e => e.Total);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        // Shared fields
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        // Estimate-specific
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.EstimatedAmount).HasPrecision(18, 2);

        // Quote-specific
        builder.Property(e => e.QuoteNumber).HasMaxLength(20);
        builder.Property(e => e.TaxRate).HasPrecision(8, 6);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        // Indexes
        builder.HasIndex(e => e.QuoteNumber).IsUnique().HasFilter("quote_number IS NOT NULL");
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.AssignedToId);
        builder.HasIndex(e => e.SourceEstimateId);

        // Relationships
        builder.HasOne(e => e.Customer)
            .WithMany(c => c.Quotes)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ShippingAddress)
            .WithMany()
            .HasForeignKey(e => e.ShippingAddressId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SourceEstimate)
            .WithOne(e => e.GeneratedQuote)
            .HasForeignKey<Quote>(e => e.SourceEstimateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Quote)
            .HasForeignKey(l => l.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
