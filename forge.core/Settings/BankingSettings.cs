namespace Forge.Core.Settings;

/// <summary>
/// BANK-002 Phase A — NACHA origination settings. These come from the Frontier CU ACH
/// origination agreement (immediate destination/origin, company ID, ODFI prefix); the file
/// generator refuses to run until the required ones are populated, so an unconfigured
/// install can never emit a malformed file. The exposure limit is the §10.1 control: a
/// batch whose total exceeds it cannot be generated.
/// </summary>
public static class BankingSettings
{
    private static readonly string Group = "Banking";

    public const string ImmediateDestinationKey = "banking.nacha.immediate-destination";
    public const string ImmediateDestinationNameKey = "banking.nacha.immediate-destination-name";
    public const string ImmediateOriginKey = "banking.nacha.immediate-origin";
    public const string ImmediateOriginNameKey = "banking.nacha.immediate-origin-name";
    public const string CompanyNameKey = "banking.nacha.company-name";
    public const string CompanyIdKey = "banking.nacha.company-id";
    public const string OriginatingDfiKey = "banking.nacha.originating-dfi";
    public const string EntryClassCodeKey = "banking.nacha.entry-class-code";
    public const string RequirePrenoteKey = "banking.require-prenote";
    public const string ExposureLimitKey = "banking.exposure-limit";
    public const string StatementMatchWindowDaysKey = "banking.statement.match-window-days";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(ImmediateDestinationKey, Group, "NACHA — Immediate Destination (bank routing)", SettingDataType.String,
            Description: "The receiving bank's 9-digit routing number for the file header (from the ACH "
                + "origination agreement — Frontier CU).",
            ValidationPattern: @"^\d{9}$",
            SortOrder: 100),
        new(ImmediateDestinationNameKey, Group, "NACHA — Immediate Destination Name", SettingDataType.String,
            Description: "The receiving bank's name as it should appear in the file header (max 23 chars).",
            SortOrder: 101),
        new(ImmediateOriginKey, Group, "NACHA — Immediate Origin", SettingDataType.String,
            Description: "Our origin identifier for the file header — usually 1 + EIN, or the value the bank "
                + "assigns in the origination agreement (10 characters).",
            ValidationPattern: @"^\d{9,10}$",
            SortOrder: 102),
        new(ImmediateOriginNameKey, Group, "NACHA — Immediate Origin Name", SettingDataType.String,
            Description: "Our company name as it should appear in the file header (max 23 chars).",
            SortOrder: 103),
        new(CompanyNameKey, Group, "NACHA — Company Name", SettingDataType.String,
            Description: "Company name on each batch header — what appears on the vendor's bank statement "
                + "(max 16 chars).",
            SortOrder: 104),
        new(CompanyIdKey, Group, "NACHA — Company ID", SettingDataType.String,
            Description: "10-character company identification on batch headers/controls — usually 1 + EIN, per "
                + "the origination agreement.",
            ValidationPattern: @"^\d{10}$",
            SortOrder: 105),
        new(OriginatingDfiKey, Group, "NACHA — Originating DFI", SettingDataType.String,
            Description: "First 8 digits of the ODFI (our bank's) routing number — prefixes every trace number.",
            ValidationPattern: @"^\d{8}$",
            SortOrder: 106),
        new(EntryClassCodeKey, Group, "NACHA — Standard Entry Class", SettingDataType.String,
            Description: "SEC code for vendor payment batches: CCD (corporate, the default) or PPD (personal "
                + "accounts, e.g. sole proprietors).",
            DefaultValue: "CCD",
            ValidationPattern: @"^(CCD|PPD)$",
            SortOrder: 107),
        new(RequirePrenoteKey, Group, "Require Prenote Verification", SettingDataType.Boolean,
            Description: "When on (the default), a vendor bank account must pass a zero-dollar prenote before "
                + "it can receive live payments. Turn off only if the bank agreement waives prenoting.",
            DefaultValue: "true",
            SortOrder: 108),
        new(ExposureLimitKey, Group, "ACH Exposure Limit", SettingDataType.String,
            Description: "Maximum total dollar amount allowed in one payment batch (the §10.1 exposure control). "
                + "0 = no limit.",
            DefaultValue: "0",
            ValidationPattern: @"^\d+(\.\d+)?$",
            SortOrder: 109),
        new(StatementMatchWindowDaysKey, Group, "Statement Auto-Match Window (days)", SettingDataType.Integer,
            Description: "BANK-001: a statement line auto-matches a GL cash line only when the amounts are "
                + "equal AND the dates are within this many days (and exactly one candidate fits).",
            DefaultValue: "5",
            SortOrder: 110),
    ];
}
