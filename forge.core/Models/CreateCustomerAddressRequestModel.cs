namespace Forge.Core.Models;

public record CreateCustomerAddressRequestModel(
    string Label,
    string AddressType,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    bool IsDefault);
