namespace Forge.Core.Settings;

/// <summary>
/// Company-wide over/under quantity tolerances for purchase receipts (and subcontract receive-backs) and for
/// sales shipments, in percent of the ordered quantity. Vendors and customers can override them, and each
/// order line can override its party's value. 0/0 (the default) means a line completes only at the exact
/// ordered quantity and nothing past it is accepted without a manager's override.
/// </summary>
public static class ToleranceSettings
{
    public const string OverReceiptPercentKey = "purchasing.over-receipt-percent";
    public const string UnderReceiptPercentKey = "purchasing.under-receipt-percent";
    public const string OverShipPercentKey = "sales.over-ship-percent";
    public const string UnderShipPercentKey = "sales.under-ship-percent";

    public const decimal MaxOverPercent = 999.99m;
    public const decimal MaxUnderPercent = 100m;

    private const string OverPattern = @"^\d{1,3}(\.\d{1,2})?$";
    private const string UnderPattern = @"^(100(\.0{1,2})?|\d{1,2}(\.\d{1,2})?)$";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } =
    [
        new(OverReceiptPercentKey, "Purchasing", "Over-receipt tolerance (%)", SettingDataType.String,
            Description: "How far past the ordered quantity a purchase-order line or subcontract receive-back may "
                + "come in without an override. Above it the receipt needs a manager's confirmation and a reason. "
                + "Each vendor and order line can override it. 0 = nothing over the ordered quantity.",
            DefaultValue: "0",
            ValidationPattern: OverPattern,
            SortOrder: 10),
        new(UnderReceiptPercentKey, "Purchasing", "Under-receipt tolerance (%)", SettingDataType.String,
            Description: "How far short of the ordered quantity a receipt still completes the line. Inside it the "
                + "rest is short-closed \"within tolerance\" and drops off open supply; below it the line stays "
                + "open on back order. Each vendor and order line can override it. 0 = only the full quantity completes it.",
            DefaultValue: "0",
            ValidationPattern: UnderPattern,
            SortOrder: 20),
        new(OverShipPercentKey, "Sales", "Over-ship tolerance (%)", SettingDataType.String,
            Description: "How far past the ordered quantity a sales-order line may ship without an override. Above "
                + "it the shipment needs a manager's confirmation and a reason. Each customer and order line can "
                + "override it. 0 = nothing over the ordered quantity.",
            DefaultValue: "0",
            ValidationPattern: OverPattern,
            SortOrder: 20),
        new(UnderShipPercentKey, "Sales", "Under-ship tolerance (%)", SettingDataType.String,
            Description: "How far short of the ordered quantity a shipment still completes the line. Inside it the "
                + "rest is closed \"within tolerance\" and drops off back order and demand; below it the line stays "
                + "open. Each customer and order line can override it. 0 = only the full quantity completes it.",
            DefaultValue: "0",
            ValidationPattern: UnderPattern,
            SortOrder: 30),
    ];
}
