using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// ⚡ BANKING BOUNDARY — a vendor's ACH destination account (BANK-002 Phase A). The routing and
/// account numbers are stored ONLY as Data-Protection-API ciphertext (purpose <c>Forge.Banking</c>);
/// the masked twins are the only representation that ever leaves the server. Decryption happens at
/// exactly one seam: NACHA file generation.
///
/// <para><b>Dual control.</b> Every create or change to the numbers lands in
/// <see cref="VendorBankAccountStatus.PendingApproval"/>; a SECOND user (never the change-maker —
/// enforced against <see cref="ChangedByUserId"/>) approves before the account can prenote or pay.
/// A change to an already-verified account resets it to PendingApproval — re-approval AND
/// re-prenote, because the destination is materially new.</para>
/// </summary>
public class VendorBankAccount : BaseAuditableEntity
{
    public int VendorId { get; set; }

    /// <summary>Display label ("Operating account"). Never derived from the real numbers.</summary>
    public string Nickname { get; set; } = string.Empty;

    public BankAccountType AccountType { get; set; } = BankAccountType.Checking;

    /// <summary>ABA routing number, Data-Protection ciphertext. Plaintext exists only inside NACHA generation.</summary>
    public string RoutingNumberEncrypted { get; set; } = string.Empty;

    /// <summary>Account number, Data-Protection ciphertext. Plaintext exists only inside NACHA generation.</summary>
    public string AccountNumberEncrypted { get; set; } = string.Empty;

    /// <summary>Masked routing for display, e.g. "•••••0301" (last 4 only).</summary>
    public string RoutingNumberMasked { get; set; } = string.Empty;

    /// <summary>Masked account for display, e.g. "••••••4321" (last 4 only).</summary>
    public string AccountNumberMasked { get; set; } = string.Empty;

    public VendorBankAccountStatus Status { get; set; } = VendorBankAccountStatus.PendingApproval;

    /// <summary>Who made the LAST change to the numbers — the user the dual-control approve must differ from.</summary>
    public int ChangedByUserId { get; set; }

    /// <summary>The distinct second user who approved the pending change.</summary>
    public int? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Set when a prenote batch containing this account is released.</summary>
    public DateTimeOffset? PrenoteSentAt { get; set; }

    /// <summary>Set when the prenote return window passed and a user marked the account verified.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }
    public int? VerifiedByUserId { get; set; }

    public Vendor Vendor { get; set; } = null!;
}
