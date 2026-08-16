namespace Forge.Core.Models.Extraction;

/// <summary>
/// Whether an extracted unit price agrees with what this customer has actually
/// been charged for this part.
///
/// <para>This is the gate's most useful signal. A fat-fingered extra zero, a
/// stale price the customer copied from an old quote, or a genuine renegotiation
/// all look identical in the email — but they look very different against
/// history, and only the first two should stop an order proposing itself.</para>
/// </summary>
public sealed record PriceCrossCheck(
    PriceCrossCheckOutcome Outcome,
    decimal? ExtractedPrice,
    decimal? ExpectedPrice,
    /// <summary>Signed fractional difference: +0.10 means the customer quoted 10% above our price.</summary>
    decimal? Variance,
    string Explanation)
{
    /// <summary>No price to check, or nothing to check it against. Not a failure — just no signal.</summary>
    public static PriceCrossCheck NotApplicable(string why) =>
        new(PriceCrossCheckOutcome.NoBasis, null, null, null, why);

    /// <summary>True only when the price positively agrees with history. Absence of a check is never agreement.</summary>
    public bool Agrees => Outcome == PriceCrossCheckOutcome.Match;
}

public enum PriceCrossCheckOutcome
{
    /// <summary>Within tolerance of the resolved price. One of the conditions for proposing a draft.</summary>
    Match,

    /// <summary>Outside tolerance. The draft is still created; it just cannot be pre-approved.</summary>
    Mismatch,

    /// <summary>No extracted price, or no pricing history for this customer and part.</summary>
    NoBasis,
}
