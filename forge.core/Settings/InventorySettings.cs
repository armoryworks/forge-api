namespace Forge.Core.Settings;

/// <summary>
/// Inventory settings. <c>inventory.surplus-location-id</c> is the bin that receives the finished pieces a
/// sales-order job makes beyond its order quantity; blank stocks everything where it goes today.
/// </summary>
public static class InventorySettings
{
    private static readonly string Group = "Inventory";

    public const string SurplusLocationKey = "inventory.surplus-location-id";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } =
    [
        new(SurplusLocationKey, Group, "Surplus stock location", SettingDataType.Integer,
            Description: "When a job made for a sales order is received to stock, the good pieces beyond the order "
                + "quantity go to this bin and the rest goes where it goes today. Surplus stock is normal counted "
                + "and costed inventory, available to sell. Jobs made to stock are not split. Blank = off.",
            DefaultValue: null,
            ValidationPattern: @"^[1-9]\d{0,9}$",
            SortOrder: 10),
    ];
}
