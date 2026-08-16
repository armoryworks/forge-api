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

    /// <summary>
    /// Replaced by a newer statement from the same party — an amended purchase
    /// order, or a cancellation. Distinct from <see cref="Revoked"/>, which is a
    /// withdrawal with nothing replacing it (entered in error, terms changed).
    /// The pairing matters in a dispute: one says the customer changed their
    /// mind, the other says we got it wrong.
    ///
    /// <para>Leaving <see cref="Accepted"/> is what keeps the production gate a
    /// single unchanged predicate — superseded rows fall out for free.</para>
    /// </summary>
    Superseded,
}
