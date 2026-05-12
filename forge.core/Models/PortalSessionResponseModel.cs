namespace Forge.Core.Models;

/// <summary>
/// Returned from <c>POST /portal/auth/exchange</c> on a successful magic-link
/// exchange. The token is a short-lived JWT (signed with the same secret the
/// employee scheme uses but carrying <c>portal_session=true</c>) that the
/// frontend treats opaquely and replays as <c>Authorization: Bearer</c>.
/// </summary>
public record PortalSessionResponseModel(
    string Token,
    DateTimeOffset ExpiresAt,
    PortalIdentityModel Identity);

public record PortalIdentityModel(
    int ContactId,
    int CustomerId,
    string CustomerName,
    string ContactFirstName,
    string ContactLastName,
    string? ContactEmail);
