using Forge.Core.Enums;

namespace Forge.Core.Interfaces.Communications;

/// <summary>
/// Decides which party an inbound address belongs to, and how confidently.
///
/// <para>Separate from <c>ICommunicationMatcher</c> because the two answer
/// different questions. The matcher asks "where do I file this"; the resolver
/// asks "whose is it, and how sure am I". Only the resolver's confidence tier
/// decides whether anything downstream may propose a draft order, so it is
/// worth being able to test and reason about on its own.</para>
/// </summary>
public interface IPartyResolver
{
    /// <summary>
    /// Resolve a sender address to a party.
    ///
    /// <para>Never guesses. An address that matches nothing returns
    /// <see cref="CommunicationMatchConfidence.Unmatched"/> with a null party
    /// rather than a best effort — the message still lands, in triage, where a
    /// human decides.</para>
    /// </summary>
    /// <param name="channel">
    /// Picks the lookup field — Email matches a contact's address, Voice matches
    /// their phone. Same tiers and the same refusal to guess either way.
    /// </param>
    Task<PartyResolution> ResolveAsync(string address, CommunicationChannel channel, CancellationToken ct);
}

/// <summary>
/// Outcome of resolving one address.
/// </summary>
/// <param name="Confidence">
/// How the party was determined. Exact means the address itself is on file.
/// Domain means only the domain is, which files the correspondence but is never
/// sufficient to act on — anyone at the domain can send mail.
/// </param>
/// <param name="PartyType">Null when unmatched.</param>
/// <param name="PartyId">Null when unmatched.</param>
/// <param name="ContactId">The specific person, when the address resolved to one.</param>
/// <param name="Reason">Human-readable explanation, shown in triage and logged.</param>
public sealed record PartyResolution(
    CommunicationMatchConfidence Confidence,
    CommunicationPartyType? PartyType,
    int? PartyId,
    int? ContactId,
    string Reason)
{
    /// <summary>Nothing on file claims this address.</summary>
    public static PartyResolution Unmatched(string reason) =>
        new(CommunicationMatchConfidence.Unmatched, null, null, null, reason);

    /// <summary>True only for <see cref="CommunicationMatchConfidence.Exact"/> — the bar for proposing anything.</summary>
    public bool IsActionable => Confidence == CommunicationMatchConfidence.Exact;
}
