namespace Forge.Core.Enums;

/// <summary>
/// Incoterms 2020 — international commerce terms defining who pays freight,
/// who insures, and when title transfers between buyer and seller. Replaces
/// the US-only "FOB origin / FOB destination" shorthand. Stored on the
/// VendorPart (default for that part from that vendor) and overridable per
/// PurchaseOrder.
///
/// <para>FOB-Origin and FOB-Destination are not separate ICC codes (FOB
/// alone is an ICC term meaning "Free on Board" at the named port of
/// shipment). They're listed here as separate enum values because that's
/// how US buyers think + speak. FOB is conceptually FOB-Origin.</para>
///
/// <para>Cost-calc behavior keys off the term — most freight-paid-by-seller
/// terms (CFR/CIF/CPT/CIP/DAP/DPU) default the line's FreightIncluded flag
/// to true; DDP also defaults DutyIncluded true. Defaults can be overridden
/// per PO if a vendor has a non-standard arrangement.</para>
/// </summary>
public enum Incoterm
{
    /// <summary>Ex Works — buyer arranges everything from seller's premises.</summary>
    EXW,
    /// <summary>Free Carrier — seller delivers to a named carrier; risk transfers there.</summary>
    FCA,
    /// <summary>Free Alongside Ship — seller delivers alongside the vessel at the named port.</summary>
    FAS,
    /// <summary>Free on Board (sea/inland-waterway only). The most common US-domestic term.</summary>
    FOB,
    /// <summary>FOB Origin — US convention; equivalent to FOB. Buyer takes title at shipping point.</summary>
    FOB_Origin,
    /// <summary>FOB Destination — US convention; seller retains title until delivery to buyer's dock.</summary>
    FOB_Destination,
    /// <summary>Cost and Freight — seller pays freight to destination port; buyer assumes risk after loading.</summary>
    CFR,
    /// <summary>Cost, Insurance and Freight — CFR plus seller-paid marine insurance.</summary>
    CIF,
    /// <summary>Carriage Paid To — seller pays freight to a named destination.</summary>
    CPT,
    /// <summary>Carriage and Insurance Paid To — CPT plus seller-paid insurance.</summary>
    CIP,
    /// <summary>Delivered at Place — seller delivers to a named place, buyer unloads.</summary>
    DAP,
    /// <summary>Delivered at Place Unloaded — like DAP but seller unloads.</summary>
    DPU,
    /// <summary>Delivered Duty Paid — seller pays everything including import duties.</summary>
    DDP,
}
