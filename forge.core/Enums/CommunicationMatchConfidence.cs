namespace Forge.Core.Enums;

/// <summary>
/// How firmly an inbound communication was tied to a party. Recorded on the row
/// so a reviewer can see WHY something was matched, and so the draft-order path
/// can require the strongest tier before proposing anything.
/// </summary>
public enum CommunicationMatchConfidence
{
    /// <summary>Sender address equals a Contact's or Lead's address exactly (normalized). The only tier that may feed a draft order.</summary>
    Exact,

    /// <summary>
    /// Sender's domain matches an enabled domain ingest rule. Good enough to
    /// file the correspondence under the party; never good enough to propose an
    /// order, because anyone at the domain can send mail.
    /// </summary>
    Domain,

    /// <summary>No rule matched. The row lands in triage with a null party and is never auto-matched.</summary>
    Unmatched,
}
