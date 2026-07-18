namespace Forge.Core.Enums;

/// <summary>Whether a barcode's value is a self-generated internal identifier or a licensed GS1 GTIN.</summary>
public enum BarcodeIdentityType
{
    /// <summary>Self-generated, unique only within this install (free — closed-loop / internal use).</summary>
    Internal,
    /// <summary>A licensed GS1 GTIN — globally unique, safe for retail / open supply chain.</summary>
    Gs1,
}
