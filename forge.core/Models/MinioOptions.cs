namespace Forge.Core.Models;

public class MinioOptions
{
    public const string SectionName = "Minio";

    public string Endpoint { get; set; } = "localhost:9000";

    /// <summary>
    /// Public-facing MinIO endpoint for presigned URLs returned to browsers.
    /// When running inside Docker, <see cref="Endpoint"/> is an internal hostname
    /// (e.g. "forge-storage:9000") that the browser cannot reach.
    /// Set this to the host-accessible address (e.g. "localhost:9000").
    /// Defaults to <see cref="Endpoint"/> when not set.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public bool UseSsl { get; set; }
    public string JobFilesBucket { get; set; } = "forge-job-files";
    public string ReceiptsBucket { get; set; } = "forge-receipts";
    public string EmployeeDocsBucket { get; set; } = "forge-employee-docs";
    public string PiiDocsBucket { get; set; } = "forge-pii-docs";
}
