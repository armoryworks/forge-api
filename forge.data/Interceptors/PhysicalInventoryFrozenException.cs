namespace Forge.Data.Interceptors;

/// <summary>
/// Thrown when a stock movement touches a location a physical inventory has frozen. The exception middleware maps
/// it to 409 with the code <c>physical-inventory-frozen</c>.
/// </summary>
public sealed class PhysicalInventoryFrozenException(string inventoryNumber, string message) : Exception(message)
{
    public const string Code = "physical-inventory-frozen";

    public string InventoryNumber { get; } = inventoryNumber;
}
