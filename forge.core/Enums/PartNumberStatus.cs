namespace Forge.Core.Enums;

/// <summary>Who, if anyone, already holds a part number, counting soft-deleted parts.</summary>
public enum PartNumberStatus
{
    /// <summary>No part has this number.</summary>
    None,
    /// <summary>A live part has this number.</summary>
    Active,
    /// <summary>Only a soft-deleted part has this number; the unique index still blocks reuse.</summary>
    Deleted,
}
