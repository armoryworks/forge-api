namespace Forge.Core.Enums;

/// <summary>
/// regulatory-watchtower / compliance-calendar A-5: lifecycle of a proposed regulatory change.
/// Watchtower proposes; an admin confirms (Applied) or dismisses — never auto-applied.
/// </summary>
public enum RegulatoryProposalStatus
{
    Pending,
    Applied,
    Dismissed,
}
