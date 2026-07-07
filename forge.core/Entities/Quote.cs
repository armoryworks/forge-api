using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class Quote : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public QuoteType Type { get; set; } = QuoteType.Quote;
    public int CustomerId { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? Notes { get; set; }
    public int? AssignedToId { get; set; }

    // Estimate-specific fields (null for Quote-type rows)
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? EstimatedAmount { get; set; }

    // Quote-specific fields (null/default for Estimate-type rows)
    public string? QuoteNumber { get; set; }
    public int? ShippingAddressId { get; set; }
    public DateTimeOffset? SentDate { get; set; }
    public DateTimeOffset? AcceptedDate { get; set; }
    public decimal TaxRate { get; set; }

    /// <summary>
    /// The customer's own purchase-order reference, captured at quote time and
    /// carried onto the SalesOrder at conversion.
    /// </summary>
    [MaxLength(50)]
    public string? CustomerPO { get; set; }

    /// <summary>
    /// The verified tax certificate justifying the current TaxRate when it
    /// deviates from the computed default (S1 document-gated tax editing).
    /// </summary>
    public int? TaxDocumentId { get; set; }

    // Conversion tracking (set on Quote-type row generated from an Estimate)
    public int? SourceEstimateId { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    // Computed (Quote-type only)
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal TaxAmount => Subtotal * TaxRate;
    public decimal Total => Subtotal + TaxAmount;

    // Navigation
    public Customer Customer { get; set; } = null!;
    public CustomerAddress? ShippingAddress { get; set; }
    public Quote? SourceEstimate { get; set; }
    public Quote? GeneratedQuote { get; set; }
    public ICollection<QuoteLine> Lines { get; set; } = [];
    public SalesOrder? SalesOrder { get; set; }
    public CustomerTaxDocument? TaxDocument { get; set; }
}
