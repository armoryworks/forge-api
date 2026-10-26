namespace Forge.Core.Enums;

/// <summary>
/// What a quote, sales-order, invoice or vendor-bill line charges for. Stored as an integer; existing
/// lines are <see cref="Item"/>.
/// </summary>
public enum DocumentLineType
{
    /// <summary>Goods or services: a part, or a free-text line.</summary>
    Item = 0,

    /// <summary>
    /// A delivery charge. Has no part, never ships or relieves inventory, and posts to Freight Income
    /// (AR) or Freight Clearing / Freight-In (AP) instead of revenue or expense.
    /// </summary>
    Freight = 1,
}
