namespace Forge.Core.Enums;

/// <summary>The kind of business object a <see cref="Entities.BusinessIdentifier"/> belongs to.</summary>
public enum BusinessEntityType
{
    Part,
    Customer,
    Vendor,
    Lead,
    SalesOrder,
    Quote,
    Invoice,
    PurchaseOrder,
    Job,
    Shipment,
    Payment,
}
