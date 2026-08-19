namespace Forge.Core.Enums;

/// <summary>How a step with several predecessors joins them: all must be complete (default) or any one.</summary>
public enum SequenceJoinPolicy
{
    All,
    Any,
}
