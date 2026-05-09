namespace QBEngineer.Core.Settings;

public static class MinioSettings
{
    public const string Group = "Storage — MinIO";

    public const string KeyMode = "minio.mode";
    public const string KeyEndpoint = "minio.endpoint";
    public const string KeyAccessKey = "minio.access-key";
    public const string KeySecretKey = "minio.secret-key";
    public const string KeyBucket = "minio.bucket";
    public const string KeyUseSsl = "minio.use-ssl";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Description: "Mock keeps uploads in-memory (data is lost on restart). Real persists to MinIO.",
            Choices: IntegrationModeChoices.All, SortOrder: 0),
        new(KeyEndpoint, Group, "Endpoint", SettingDataType.String,
            DefaultValue: "qb-engineer-storage:9000",
            Description: "host:port of the MinIO instance. In Docker compose this is qb-engineer-storage:9000.",
            SortOrder: 10),
        new(KeyAccessKey, Group, "Access Key", SettingDataType.String, SortOrder: 11),
        new(KeySecretKey, Group, "Secret Key", SettingDataType.Secret, IsSecret: true, SortOrder: 12),
        new(KeyBucket, Group, "Bucket Name", SettingDataType.String,
            DefaultValue: "qb-engineer", SortOrder: 13),
        new(KeyUseSsl, Group, "Use SSL", SettingDataType.Boolean,
            DefaultValue: "false",
            Description: "Off for in-cluster Docker; ON when MinIO is behind a TLS reverse proxy.",
            SortOrder: 14),
    ];
}
