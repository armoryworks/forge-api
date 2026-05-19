namespace Forge.Core.Settings;

public static class MinioSettings
{
    public const string Group = "Storage — MinIO";

    public const string KeyMode = "minio.mode";
    public const string KeyEndpoint = "minio.endpoint";
    public const string KeyPublicEndpoint = "minio.public-endpoint";
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
        new(KeyEndpoint, Group, "Internal Endpoint", SettingDataType.String,
            DefaultValue: "forge-storage:9000",
            Description: "host:port the API uses to reach MinIO (server-side). In Docker compose this is forge-storage:9000.",
            SortOrder: 10),
        // Two-endpoint pattern (matches S3-compatible setups): the API
        // calls MinIO at the Internal Endpoint above; the API hands user
        // browsers presigned download URLs built against the Public
        // Endpoint here. In dogfood prod they're typically different —
        // internal is forge-storage:9000, public is whatever the reverse
        // proxy / public hostname is. Leave blank to reuse the internal
        // endpoint (only works when browsers can reach forge-storage
        // directly — i.e. localhost dev with port forwarding).
        new(KeyPublicEndpoint, Group, "Public Endpoint", SettingDataType.String,
            DefaultValue: "localhost:9000",
            Description: "host:port that end-user browsers use to download files via presigned URLs. Set to your reverse-proxy / public hostname in production (e.g. files.example.com). Leave as localhost:9000 only for local development.",
            SortOrder: 11),
        new(KeyAccessKey, Group, "Access Key", SettingDataType.String, SortOrder: 12),
        new(KeySecretKey, Group, "Secret Key", SettingDataType.Secret, IsSecret: true, SortOrder: 13),
        new(KeyBucket, Group, "Bucket Name", SettingDataType.String,
            DefaultValue: "forge", SortOrder: 14),
        new(KeyUseSsl, Group, "Use SSL", SettingDataType.Boolean,
            DefaultValue: "false",
            Description: "Off for in-cluster Docker; ON when MinIO is behind a TLS reverse proxy.",
            SortOrder: 15),
    ];
}
