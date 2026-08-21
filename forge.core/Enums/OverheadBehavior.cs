namespace Forge.Core.Enums;

/// <summary>Describes how an overhead pool's total cost responds to changes in activity volume.</summary>
public enum OverheadBehavior
{
    /// <summary>Total cost does not vary with volume.</summary>
    Fixed,
    /// <summary>Total cost varies proportionally with volume.</summary>
    Variable,
    /// <summary>A mix of a fixed portion and a variable portion.</summary>
    Semi
}
