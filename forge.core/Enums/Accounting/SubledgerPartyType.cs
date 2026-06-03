namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Polymorphic counterparty type for a control-account <c>JournalLine</c>. The
/// party fields stay polymorphic (a control line's counterparty is a Customer
/// <b>or</b> a Vendor) so they cannot be FK-enforced; the posting engine
/// requires them on control lines (§5.1, §5.2).
/// </summary>
public enum SubledgerPartyType
{
    Customer,
    Vendor,
}
