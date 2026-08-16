namespace Forge.Core.Enums;

/// <summary>
/// Direction from the tenant's perspective.
///
/// <para>Named Flow rather than Direction because
/// <c>Forge.Core.Models.Communications.CommunicationDirection</c> already exists
/// as the wire-envelope enum. Keeping them distinct avoids a using-alias at every
/// call site and lets the persisted values evolve independently of the adapter
/// contract.</para>
/// </summary>
public enum CommunicationFlow
{
    /// <summary>External party → tenant. The From address is the one that identifies the party.</summary>
    Inbound,
    /// <summary>Tenant → external party. The To addresses identify the party.</summary>
    Outbound,
}
