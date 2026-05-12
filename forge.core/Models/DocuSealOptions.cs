namespace Forge.Core.Models;

public class DocuSealOptions
{
    public const string SectionName = "DocuSeal";

    public string BaseUrl { get; set; } = "http://forge-signing:3000";

    /// <summary>
    /// Browser-reachable base URL for DocuSeal (e.g., the nginx proxy path).
    /// Used to rewrite embed_src values before returning them to the frontend.
    /// When empty, embed URLs are returned unchanged (only works if DocuSeal is
    /// directly browser-accessible on BaseUrl — not the case in Docker deployments).
    /// Example: "http://localhost:4200/docuseal" or "https://app.example.com/docuseal"
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public string WebhookSecret { get; set; } = string.Empty;
}
