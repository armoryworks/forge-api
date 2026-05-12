namespace Forge.Core.Models;

public class LocalStorageOptions
{
    public const string SectionName = "LocalStorage";

    /// <summary>
    /// Root path inside the container where files are stored.
    /// Map this to a host directory via a Docker volume mount.
    /// Default: /app/storage
    /// </summary>
    public string RootPath { get; set; } = "/app/storage";

    /// <summary>
    /// Public-facing base URL the browser uses to download files.
    /// Must point to the API host so the StorageController can serve them.
    /// Example: http://localhost:5000
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>
    /// Lifetime (in seconds) of presigned download URLs.
    /// Default: 3600 (1 hour).
    /// </summary>
    public int PresignedUrlExpirySeconds { get; set; } = 3600;
}
