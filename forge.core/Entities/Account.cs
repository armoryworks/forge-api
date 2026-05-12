namespace Forge.Core.Entities;

/// <summary>
/// Phase 1r / Batch 12 — pre-customer company-level grouping. An
/// Account represents a target organization across multiple
/// stakeholder contacts; before a deal closes, a manufacturer
/// typically courts engineer + procurement + plant manager at the
/// same company. The legacy flat Lead row (one Lead = one
/// companyName + one contactName) couldn't model that — sales reps
/// duplicated rows per contact, splitting activity history.
///
/// Pragmatic introduction: Account is OPTIONAL. New leads can hang
/// off an Account; existing leads keep working unchanged. When a
/// Lead converts to Customer, the Account's contacts roll forward
/// alongside (handled in the convert-lead handler).
///
/// Existing customer rows aren't migrated to Accounts. Customer is
/// the post-conversion authoritative entity; Account is the pre-
/// conversion staging entity. Different lifecycles.
/// </summary>
public class Account : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    /// <summary>Employee-count bucket — feeds the ICP-rubric size dimension.</summary>
    public string? SizeBracket { get; set; }
    /// <summary>Rep ownership across the whole account (overridden per-lead when assignment rules fire).</summary>
    public int? OwnerUserId { get; set; }

    public ICollection<AccountContact> Contacts { get; set; } = [];
}

/// <summary>
/// Multi-contact-per-account. Mirrors the Customer.Contacts pattern but
/// pre-conversion. Carries the same fields a Contact does plus a Role
/// hint so reps can see at a glance who's the engineer vs. procurement.
/// </summary>
public class AccountContact : BaseAuditableEntity
{
    public int AccountId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Stakeholder role hint — engineer / procurement / plant-manager / decision-maker / champion / other.</summary>
    public string? Role { get; set; }
    public bool IsPrimary { get; set; }

    public Account Account { get; set; } = null!;
}
