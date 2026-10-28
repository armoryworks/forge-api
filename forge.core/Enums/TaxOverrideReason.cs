namespace Forge.Core.Enums;

/// <summary>Why a document's tax rate differs from the rate computed for its customer. Stored as an integer.</summary>
public enum TaxOverrideReason
{
    Resale = 0,
    ExemptOrganization = 1,
    Government = 2,
    OutOfJurisdiction = 3,
    Other = 4,
}
