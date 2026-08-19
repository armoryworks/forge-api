namespace Forge.Core.Enums;

/// <summary>What a gate currently reads. Unknown = never evaluated (or its source could not answer).</summary>
public enum SequenceGateVerdict
{
    Unknown,
    Go,
    NoGo,
}
