namespace Forge.Core.Models;

/// <summary>
/// Minimal vendor row for pickers used by roles that may reference a vendor but not read the
/// vendor master: id, company name and vendor number only.
/// </summary>
public record VendorLookupModel(int Id, string Name, string? Code);
