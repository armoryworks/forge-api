namespace Forge.Core.Enums;

/// <summary>Lifecycle of an imported marketplace payout, from arrival to tie-out.</summary>
public enum ChannelSettlementStatus
{
    /// <summary>Imported from the channel; lines present but not yet matched to orders.</summary>
    Imported,

    /// <summary>Every line that should reference an order does, and the line sum equals the reported net.</summary>
    Reconciled,

    /// <summary>Lines sum to something other than the reported net, or order-linked lines reference orders that do not exist. Needs a human.</summary>
    Discrepancy,

    /// <summary>Reviewed and accepted despite a variance, with the reason recorded.</summary>
    Accepted,
}
