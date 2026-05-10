namespace QBEngineer.Core.Entities;

/// <summary>
/// One-row-per-contact authorization for the customer portal. The contact's
/// email is the login identifier; on each login attempt we generate a fresh
/// <see cref="OneTimeTokenHash"/> with a 15-minute expiry. The contact clicks
/// the magic link, the portal exchanges the one-time token for a short-lived
/// JWT (carrying <c>portal_session=true</c> + <c>customer_id</c> +
/// <c>contact_id</c> claims), and the one-time token is cleared.
///
/// `IsEnabled` is the operator's gate — admin can revoke a contact's portal
/// access without deleting the contact itself.
///
/// LastLoginAt is updated on each successful exchange so admin can surface
/// "this contact has never logged in" in a UI later.
/// </summary>
public class CustomerPortalAccess : BaseAuditableEntity
{
    public int ContactId { get; set; }
    public int CustomerId { get; set; }

    /// <summary>SHA-256 hash of the active one-time login token. Null when no login is pending.</summary>
    public string? OneTimeTokenHash { get; set; }

    public DateTimeOffset? OneTimeTokenExpiresAt { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    public Contact Contact { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
}
