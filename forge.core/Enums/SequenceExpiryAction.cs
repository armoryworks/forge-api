namespace Forge.Core.Enums;

/// <summary>What happens when a clock (resource or step dwell) expires.</summary>
public enum SequenceExpiryAction
{
    /// <summary>The dependent gate re-evaluates to NoGo and the branch cannot proceed until resolved (default).</summary>
    Block,
    /// <summary>The resource/step is flagged for review; the branch is not blocked.</summary>
    Flag,
    /// <summary>A notification is routed to the configured role, in addition to blocking.</summary>
    Escalate,
}
