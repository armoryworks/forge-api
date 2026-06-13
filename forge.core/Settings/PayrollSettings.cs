namespace Forge.Core.Settings;

/// <summary>
/// PAY-001 — payroll register import mapping overrides. The parser auto-detects columns by
/// synonym; these admin-editable settings pin EXACT header names (comma-separated when the
/// provider splits a category across columns, e.g. "Social Security,Medicare"). Choosing a
/// payroll provider is therefore a mapping decision, never code.
/// </summary>
public static class PayrollSettings
{
    private static readonly string Group = "Payroll";

    public const string EmployeeColumnKey = "payroll.register.column.employee";
    public const string GrossColumnKey = "payroll.register.column.gross";
    public const string FederalColumnKey = "payroll.register.column.federal";
    public const string StateColumnKey = "payroll.register.column.state";
    public const string FicaEmployeeColumnKey = "payroll.register.column.fica-employee";
    public const string OtherDeductionsColumnKey = "payroll.register.column.other-deductions";
    public const string EmployerTaxColumnKey = "payroll.register.column.employer-tax";
    public const string NetColumnKey = "payroll.register.column.net";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        Column(EmployeeColumnKey, "Employee", 100),
        Column(GrossColumnKey, "Gross Pay", 101),
        Column(FederalColumnKey, "Federal Withholding", 102),
        Column(StateColumnKey, "State Withholding", 103),
        Column(FicaEmployeeColumnKey, "FICA (Employee)", 104),
        Column(OtherDeductionsColumnKey, "Other Deductions", 105),
        Column(EmployerTaxColumnKey, "Employer Taxes", 106),
        Column(NetColumnKey, "Net Pay", 107),
    ];

    private static SettingDescriptor Column(string key, string label, int sortOrder)
        => new(key, Group, $"Register Column — {label}", SettingDataType.String,
            Description: "Exact CSV header name(s) from the payroll provider's register export "
                + "(comma-separated to SUM split columns). Blank = auto-detect by common synonyms.",
            SortOrder: sortOrder);
}
