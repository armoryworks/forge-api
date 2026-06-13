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
    public const string BalancedFilesKey = "banking.nacha.balanced";
    public const string OffsetRoutingKey = "banking.nacha.offset-routing";
    public const string OffsetAccountKey = "banking.nacha.offset-account";
    public const string ChannelKey = "banking.nacha.channel";
    public const string SftpHostKey = "banking.sftp.host";
    public const string SftpPortKey = "banking.sftp.port";
    public const string SftpUsernameKey = "banking.sftp.username";
    public const string SftpPasswordKey = "banking.sftp.password";
    public const string SftpUploadDirKey = "banking.sftp.upload-dir";
    public const string SftpReturnsDirKey = "banking.sftp.returns-dir";
    public const string WireManualAttestationKey = "banking.wire.manual-attestation";

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
        // ── Bank-portability knobs: the three places banks actually differ ──
        new(BalancedFilesKey, Group, "NACHA — Balanced Files", SettingDataType.Boolean,
            Description: "Off (default): credits-only files (service class 220) — the bank creates the "
                + "offset to your account. On: each batch carries its own offsetting DEBIT entry against "
                + "the offset account below (service class 200) — required by some banks.",
            DefaultValue: "false",
            SortOrder: 111),
        new(OffsetRoutingKey, Group, "NACHA — Offset Account Routing", SettingDataType.String,
            Description: "Our funding account's routing number — the offset debit's destination when "
                + "balanced files are on.",
            ValidationPattern: @"^\d{9}$",
            SortOrder: 112),
        new(OffsetAccountKey, Group, "NACHA — Offset Account Number", SettingDataType.Secret,
            Description: "Our funding account number for the offset debit (balanced files only). Stored "
                + "encrypted.",
            IsSecret: true,
            SortOrder: 113),
        new(ChannelKey, Group, "NACHA — Submission Channel", SettingDataType.String,
            Description: "manual (default): generate → download → upload to the bank portal by hand; "
                + "release attests the upload. sftp: releasing a batch UPLOADS the file over SFTP "
                + "(settings below) — release is still the second-user SoD step.",
            DefaultValue: "manual",
            ValidationPattern: @"^(manual|sftp)$",
            SortOrder: 114),
        new(SftpHostKey, Group, "Bank SFTP — Host", SettingDataType.String, SortOrder: 115),
        new(SftpPortKey, Group, "Bank SFTP — Port", SettingDataType.Integer, DefaultValue: "22", SortOrder: 116),
        new(SftpUsernameKey, Group, "Bank SFTP — Username", SettingDataType.String, SortOrder: 117),
        new(SftpPasswordKey, Group, "Bank SFTP — Password", SettingDataType.Secret,
            Description: "Stored encrypted. (Key-based auth: ask before wiring — most CU drops are "
                + "password-over-SSH today.)",
            IsSecret: true,
            SortOrder: 118),
        new(SftpUploadDirKey, Group, "Bank SFTP — Upload Directory", SettingDataType.String,
            DefaultValue: "/inbound", SortOrder: 119),
        new(SftpReturnsDirKey, Group, "Bank SFTP — Returns Directory", SettingDataType.String,
            DefaultValue: "/outbound", SortOrder: 120),
        new(WireManualAttestationKey, Group, "Wires — Manual Attestation", SettingDataType.Boolean,
            Description: "On: wire payments wait as Queued until a SECOND user attests the wire was "
                + "entered at the bank portal (no fake auto-success). Off (default): the development "
                + "mock channel processes them. Production installs should turn this ON until a real "
                + "bank wire API exists.",
            DefaultValue: "false",
            SortOrder: 121),
    ];
}
