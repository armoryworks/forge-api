using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One payout batch from a marketplace, and the reason the bank deposit ties to
/// anything at all.
///
/// <para>Account business settles per document: an invoice goes out, a payment
/// comes in, they apply 1:1. Marketplaces do not. They pay on their own cycle,
/// in one lump, net of referral fees, fulfilment fees, postage, refunds and
/// account charges — components that arrive on a different cadence than the
/// orders that caused them. Without this record the deposit from Amazon matches
/// no invoice in the system and the install is left reconciling by hand
/// forever.</para>
///
/// <para>⚡ ACCOUNTING BOUNDARY — settlement is an operational reconciliation
/// record in every mode. In connected-accounting mode the resulting journal
/// lives in the external system; in built-in mode the posting engine consumes
/// this. The entity itself is always app-resident because it is what the
/// channel connector writes.</para>
/// </summary>
public class ChannelSettlement : BaseAuditableEntity
{
    public int ChannelId { get; set; }
    public SalesChannel Channel { get; set; } = null!;

    /// <summary>The channel's own settlement/payout id. Unique per channel — the idempotency key for re-import.</summary>
    public string ExternalSettlementId { get; set; } = string.Empty;

    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }

    /// <summary>When the channel says the money was deposited. Null until it reports one.</summary>
    public DateTimeOffset? DepositedAt { get; set; }

    /// <summary>Net amount the channel reports paying out. The reconciliation target.</summary>
    public decimal ReportedNetAmount { get; set; }

    /// <summary>ISO 4217 code the settlement is denominated in.</summary>
    public string CurrencyCode { get; set; } = "USD";

    public ChannelSettlementStatus Status { get; set; } = ChannelSettlementStatus.Imported;

    /// <summary>Why a variance was accepted, when <see cref="Status"/> is <see cref="ChannelSettlementStatus.Accepted"/>.</summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>Raw payload as received, kept so a reconciliation dispute can be re-derived from source.</summary>
    public string? RawPayloadJson { get; set; }

    public ICollection<ChannelSettlementLine> Lines { get; set; } = [];

    /// <summary>Sum of the imported components. Equals <see cref="ReportedNetAmount"/> when the batch ties out.</summary>
    public decimal ComputedNetAmount => Lines.Sum(l => l.Amount);

    /// <summary>Signed difference between what the channel says it paid and what its own line detail adds up to.</summary>
    public decimal Variance => ReportedNetAmount - ComputedNetAmount;
}
