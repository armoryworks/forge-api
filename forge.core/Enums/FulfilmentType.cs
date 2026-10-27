namespace Forge.Core.Enums;

/// <summary>
/// How goods reach the customer. Standard ships from our own stock; DropShip has the vendor ship straight to the
/// customer, so nothing passes through a bin; BackToBack buys the goods for one sales order line into our
/// warehouse, where the receipt is reserved to that line.
/// </summary>
public enum FulfilmentType
{
    Standard,
    DropShip,
    BackToBack,
}
