using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;

namespace QBEngineer.Core.Entities;

public class Vendor : BaseAuditableEntity, IActiveAware
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Captured when <see cref="IsActive"/> transitions from true → false.
    /// Cleared when reactivated. POs already issued (status != Draft) before
    /// this date may still be received/closed normally; new POs are blocked.
    /// (Phase 3 H2 / WU-12 — vendor-lifecycle grace window.)
    /// </summary>
    public DateTimeOffset? DeactivationDate { get; set; }

    public AutoPoMode? AutoPoMode { get; set; }
    public decimal? MinOrderAmount { get; set; }

    /// <summary>
    /// Bought-parts effort PR4 — per-vendor override for the off-tier
    /// price-variance prompt at PO line entry. When the buyer enters a
    /// unit price that differs from the current effective tier by more
    /// than this percentage, the prompt offers an exception / update-tiers
    /// / cancel choice. Null falls back to the system-wide default in
    /// <c>SystemSetting</c> key <c>purchasing.offTierVariancePct</c>
    /// (5% out of the box). A wider tolerance lets quirky vendors with
    /// noisy pricing skip prompts that aren't actionable.
    /// </summary>
    public decimal? OffTierVariancePct { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = [];

    // IActiveAware — used by Phase 3 H2 active-check on transaction creation.
    public bool IsActiveForNewTransactions => IsActive;
    public string GetDisplayName() => CompanyName;
}
