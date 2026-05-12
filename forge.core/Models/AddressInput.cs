namespace Forge.Core.Models;

/// <summary>
/// Lightweight nested address input used at create-time to capture a customer's
/// billing/shipping addresses in a single POST. Maps onto the existing
/// <c>CustomerAddress</c> entity at handler time. Phase 3 F3.
///
/// The shape uses the small-shop-friendly field names from the F3 spec
/// (<c>street</c>, <c>city</c>, <c>state</c>, <c>postal</c>, <c>country</c>);
/// the underlying entity stores them as Line1/City/State/PostalCode/Country.
/// </summary>
public record AddressInput(
    string Street,
    string? Line2,
    string City,
    string State,
    string Postal,
    string? Country
);
