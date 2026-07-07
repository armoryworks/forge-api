namespace Forge.Core.Models;

public record CustomerAddressResponseModel(
    int Id,
    string Label,
    string AddressType,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    bool IsDefault,
    bool IsActive = true);
