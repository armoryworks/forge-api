namespace Forge.Core.Enums;

/// <summary>The activity measure used to absorb an overhead pool into product cost.</summary>
public enum OverheadDriver
{
    /// <summary>Machine hours consumed.</summary>
    MachineHour,
    /// <summary>Direct labor hours consumed.</summary>
    LaborHour,
    /// <summary>Direct labor dollars incurred.</summary>
    LaborDollar,
    /// <summary>Material dollars consumed.</summary>
    MaterialDollar,
    /// <summary>Units produced.</summary>
    Unit,
    /// <summary>Count of receipts processed.</summary>
    ReceiptCount
}
