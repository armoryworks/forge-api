namespace Forge.Core.Models;

/// <summary>
/// Destination for a retail order, snapshotted onto the order rather than
/// stored in the customer address book — see <see cref="Entities.OrderShipTo"/>
/// for why.
/// </summary>
public record OrderShipToInput
{
    public string Name { get; init; } = string.Empty;
    public string? Company { get; init; }
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = "US";
    public string? Phone { get; init; }

    /// <summary>Set by importers when the channel already validated deliverability, to skip a redundant USPS round trip.</summary>
    public bool IsValidated { get; init; }
}
