namespace Forge.Core.Enums;

/// <summary>
/// Which master-data record a communication or attestation belongs to.
///
/// <para>Deliberately wider than Contact. The pre-existing
/// <c>ContactInteraction</c> could only hang off a Contact, which meant vendor
/// correspondence had nowhere to live and mail from an address that matched a
/// customer's domain but no named contact was simply dropped.</para>
/// </summary>
public enum CommunicationPartyType
{
    Customer,
    Vendor,
    /// <summary>A named person at a customer. Narrower than <see cref="Customer"/>, and preferred when known.</summary>
    Contact,
}
