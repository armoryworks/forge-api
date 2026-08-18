namespace Forge.Core.Enums;

/// <summary>
/// How a barcode row came to exist: the entity's single auto-generated code, or a
/// user-added alternate value that coexists with it.
/// </summary>
public enum BarcodeSource
{
    /// <summary>Auto-generated and auto-maintained — exactly one per entity, kept in sync with the
    /// entity's identity (part number / GTIN), and not user-removable.</summary>
    System,

    /// <summary>A manually-added alternate value (a manufacturer UPC, a vendor SKU, a legacy label)
    /// that coexists with the system code, is resolvable on scan, and is user-removable.</summary>
    Manual,
}
