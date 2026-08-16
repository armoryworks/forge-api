namespace Forge.Core.Enums;

/// <summary>
/// Transport a communication arrived through. Distinct from
/// <see cref="InteractionType"/>, which is the CRM-flavoured classification a
/// human picks (Call / Email / Meeting / Note) — a Meeting has no transport and
/// a Portal submission is not a meeting.
/// </summary>
public enum CommunicationChannel
{
    Email,
    Voice,
    /// <summary>Customer acted in the Forge portal — quote acceptance, public accept link.</summary>
    Portal,
}
