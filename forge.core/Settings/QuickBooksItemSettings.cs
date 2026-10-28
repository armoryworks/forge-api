namespace Forge.Core.Settings;

/// <summary>
/// The QuickBooks references documents sync against in connected mode: the item freight lines post to, the item a
/// write-off credit memo is booked against, and the item and bank account a refund receipt uses. Empty until mapped;
/// the sync that needs one refuses until it is set.
/// </summary>
public static class QuickBooksItemSettings
{
    private const string Group = "QuickBooks items";

    public const string FreightItemKey = "qbo.freight_item_id";
    public const string BadDebtItemKey = "qbo.bad_debt_item_id";
    public const string RefundItemKey = "qbo.refund_item_id";
    public const string RefundAccountKey = "qbo.refund_account_id";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } =
    [
        new(FreightItemKey, Group, "Freight item", SettingDataType.String,
            Description: "QuickBooks item ID that freight lines sync to. Empty uses QuickBooks' shipping item.",
            SortOrder: 10),
        new(BadDebtItemKey, Group, "Bad debt item", SettingDataType.String,
            Description: "QuickBooks item ID a write-off credit memo is booked against (an item pointing at bad debt).",
            SortOrder: 20),
        new(RefundItemKey, Group, "Refund item", SettingDataType.String,
            Description: "QuickBooks item ID a refund receipt line is booked against.",
            SortOrder: 30),
        new(RefundAccountKey, Group, "Refund bank account", SettingDataType.String,
            Description: "QuickBooks bank account ID a refund receipt is paid from.",
            SortOrder: 40),
    ];
}
