namespace Forge.Core.Models;

public record ValidateAddressRequestModel(
    string Street,
    string City,
    string State,
    string Zip,
    string Country);
