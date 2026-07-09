namespace Forge.Core.Enums;

/// <summary>Lifecycle of a Sales Order customer-acceptance record.</summary>
public enum AcceptanceStatus
{
    /// <summary>Request sent / awaiting the customer (portal or e-signature in flight).</summary>
    Pending,
    /// <summary>Customer accepted — this is what satisfies the production gate.</summary>
    Accepted,
    /// <summary>Customer declined the terms.</summary>
    Declined,
    /// <summary>A previously-accepted record was revoked (e.g. terms changed / entered in error).</summary>
    Revoked,
    /// <summary>A pending request lapsed without a response.</summary>
    Expired,
}
