namespace Forge.Core.Settings;

/// <summary>
/// Credit memo and return settings. <c>rma.restocking_fee_percent</c> is the restocking fee offered when a return is
/// credited (0 = none); <c>ar.writeoff_reverses_tax</c> moves the tax share of a write-off off Sales Tax Payable
/// instead of charging the whole amount to Bad Debt Expense.
/// </summary>
public static class CreditMemoSettings
{
    public const string RestockingFeePercentKey = "rma.restocking_fee_percent";
    public const string WriteOffReversesTaxKey = "ar.writeoff_reverses_tax";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } = new[]
    {
        new SettingDescriptor(
            Key: RestockingFeePercentKey,
            Group: "Payments",
            DisplayName: "Return restocking fee percent",
            DataType: SettingDataType.String,
            Description: "Percent of the credited goods kept back as a restocking fee when a return is credited. 0 for none.",
            DefaultValue: "0",
            ValidationPattern: @"^\d{1,2}(\.\d{1,2})?$",
            SortOrder: 40),
        new SettingDescriptor(
            Key: WriteOffReversesTaxKey,
            Group: "Payments",
            DisplayName: "Write-offs reverse sales tax",
            DataType: SettingDataType.Boolean,
            Description: "When on, the tax part of a written-off balance is taken off Sales Tax Payable; when off, the whole balance goes to Bad Debt Expense and bad-debt tax relief is claimed on the tax return.",
            DefaultValue: "false",
            SortOrder: 50),
    };
}
