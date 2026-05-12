namespace Forge.Core.Models;

public record ShippingAddress(
    string Name,
    string Street,
    string City,
    string State,
    string Zip,
    string Country);
