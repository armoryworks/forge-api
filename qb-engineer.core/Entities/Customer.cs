using QBEngineer.Core.Interfaces;

namespace QBEngineer.Core.Entities;

public class Customer : BaseAuditableEntity, IActiveAware
{
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Captured when <see cref="IsActive"/> transitions from true → false.
    /// Cleared on reactivation. (Phase 3 H2 / WU-12 — lifecycle grace window.)
    /// </summary>
    public DateTimeOffset? DeactivationDate { get; set; }

    // IActiveAware — used by Phase 3 H2 active-check on transaction creation.
    public bool IsActiveForNewTransactions => IsActive;
    public string GetDisplayName() => string.IsNullOrWhiteSpace(CompanyName) ? Name : CompanyName;

    // Credit management
    public decimal? CreditLimit { get; set; }
    public bool IsOnCreditHold { get; set; }
    public string? CreditHoldReason { get; set; }
    public DateTimeOffset? CreditHoldAt { get; set; }
    public int? CreditHoldById { get; set; }
    public DateTimeOffset? LastCreditReviewDate { get; set; }
    public int? CreditReviewFrequencyDays { get; set; }

    // Tax handling — many B2B manufacturing customers are sales-tax-exempt
    // (resellers, government, non-profits). When IsTaxExempt is true, invoice
    // generation must skip the sales-tax line. The exemption ID is the
    // certificate number kept on file for audit purposes.
    public bool IsTaxExempt { get; set; }
    public string? TaxExemptionId { get; set; }

    // Default tax + currency used when invoicing this customer if the line/header
    // does not specify otherwise. Both nullable so the tenant default applies.
    // Phase 3 F3 — captured at create-time to avoid a PATCH-after-create round trip.
    public int? DefaultTaxCodeId { get; set; }
    public SalesTaxRate? DefaultTaxCode { get; set; }
    /// <summary>ISO 4217 3-letter currency code (e.g. "USD"). Null = use tenant default.</summary>
    public string? DefaultCurrency { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    /// <summary>
    /// Reverse navigation to the lead this customer was converted from, if
    /// any. The FK lives on <see cref="Lead.ConvertedCustomerId"/> — no
    /// duplicate column on the customer table. Lets the Customer detail
    /// surface "Converted from Lead #N" and the Activity tab carry
    /// conversion-time provenance forward rather than treating the
    /// customer as having appeared from nowhere.
    /// </summary>
    public Lead? SourceLead { get; set; }

    // ── Phase 1r / Batch 15 — regulated-industry flags ──
    // Drive auto-flag-to-QA-team workflow on lead/customer creation
    // and gate certain technical exchanges. Each boolean signals
    // "this customer operates under this regime"; the cert/audit
    // workflow tied to each is a separate UI surface.

    /// <summary>FDA-regulated (medical device, pharma, etc.). Triggers QA review of cap fit.</summary>
    public bool IsFdaRegulated { get; set; }

    /// <summary>AS9100 (aerospace) certified work. Affects part documentation requirements.</summary>
    public bool IsAerospace { get; set; }

    /// <summary>IATF 16949 (automotive). Affects PPAP requirements + traceability rigor.</summary>
    public bool IsAutomotive { get; set; }

    /// <summary>ITAR-controlled (defense). Gates export-control clearance on every related lead.</summary>
    public bool IsItarControlled { get; set; }

    public ICollection<Contact> Contacts { get; set; } = [];
    public ICollection<Job> Jobs { get; set; } = [];
    public ICollection<CustomerAddress> Addresses { get; set; } = [];
    public ICollection<SalesOrder> SalesOrders { get; set; } = [];
    public ICollection<Quote> Quotes { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<PriceList> PriceLists { get; set; } = [];
    public ICollection<RecurringOrder> RecurringOrders { get; set; } = [];
}
