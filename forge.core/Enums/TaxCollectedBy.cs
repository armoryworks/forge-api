namespace Forge.Core.Enums;

/// <summary>
/// Who collected the sales tax on a transaction, and therefore who owes it to
/// the taxing authority.
///
/// <para>This is not cosmetic. Under US marketplace-facilitator law the
/// marketplace — not the seller — collects and remits sales tax on orders it
/// brokers. If marketplace-collected tax flows through the normal
/// <c>TaxRate → TaxAmount</c> path it lands in the seller's sales-tax-payable
/// and the install over-reports its liability. Tagging the order and its
/// invoice with <see cref="Marketplace"/> keeps that amount a pass-through: it
/// is still shown on the document (the buyer paid it) but it is never the
/// install's payable.</para>
/// </summary>
public enum TaxCollectedBy
{
    /// <summary>
    /// The install collected the tax and owes it. Every B2B order and every
    /// direct-retail order where you are the merchant of record.
    /// </summary>
    Seller,

    /// <summary>
    /// A marketplace facilitator collected and remits the tax. Pass-through —
    /// excluded from the install's sales-tax liability.
    /// </summary>
    Marketplace,
}
