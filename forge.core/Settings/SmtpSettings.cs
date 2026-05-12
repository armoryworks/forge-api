namespace Forge.Core.Settings;

public static class SmtpSettings
{
    public const string Group = "Email — Outbound (SMTP)";

    public const string KeyMode = "smtp.mode";
    public const string KeyHost = "smtp.host";
    public const string KeyPort = "smtp.port";
    public const string KeyUseSsl = "smtp.use-ssl";
    public const string KeyUsername = "smtp.username";
    public const string KeyPassword = "smtp.password";
    public const string KeyFromAddress = "smtp.from-address";
    public const string KeyFromName = "smtp.from-name";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode",
            DataType: SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Description: "Mock logs outbound emails to the integration outbox without sending.",
            Choices: IntegrationModeChoices.All,
            SortOrder: 0),
        new(KeyHost, Group, "Host", SettingDataType.String,
            Description: "SMTP server hostname (e.g. smtp.sendgrid.net, smtp.gmail.com).",
            SortOrder: 10),
        new(KeyPort, Group, "Port", SettingDataType.Integer,
            DefaultValue: "587",
            Description: "Typically 587 (STARTTLS) or 465 (SSL).",
            SortOrder: 11),
        new(KeyUseSsl, Group, "Use SSL/TLS", SettingDataType.Boolean,
            DefaultValue: "true", SortOrder: 12),
        new(KeyUsername, Group, "Username", SettingDataType.String, SortOrder: 20),
        new(KeyPassword, Group, "Password", SettingDataType.Secret, IsSecret: true, SortOrder: 21),
        new(KeyFromAddress, Group, "From Address", SettingDataType.String,
            Description: "Address shown as sender on outbound emails.",
            SortOrder: 30),
        new(KeyFromName, Group, "From Name", SettingDataType.String, SortOrder: 31),
    ];
}
