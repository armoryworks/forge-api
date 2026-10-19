using System.Text.RegularExpressions;

using Microsoft.Net.Http.Headers;

using Forge.Core.Models;

namespace Forge.Api.Features.Files;

/// <summary>
/// Single source of truth for which entity types accept file uploads and which
/// MinIO bucket each routes to. The single and chunked upload paths previously
/// carried duplicated copies of both — they drifted, and `sales-orders` missing
/// from the whitelist rejected every file on the SO Documents tab (external QA
/// report; the .png in the report was incidental). Sales and purchasing
/// document types (sales orders, customers, quotes, purchase orders) and lot
/// certificates share the job-files bucket.
/// </summary>
public static class FileEntityTypes
{
    public static readonly HashSet<string> Valid =
    [
        "jobs", "expenses", "assets", "parts", "leads", "employees",
        "identity-docs", "employee-docs", "compliance-templates",
        "pay-stubs", "tax-documents",
        "sales-orders", "customers", "quotes", "purchase-orders",
        "lots",
    ];

    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msc", ".sys", ".drv",
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".mjs", ".wsf", ".wsh",
        ".hta", ".jar", ".lnk", ".reg", ".inf", ".scf", ".url", ".appx", ".msix", ".apk", ".app",
        ".sh", ".bash", ".csh", ".ksh", ".command", ".run", ".bin", ".elf", ".so", ".dylib",
        ".html", ".htm", ".xhtml", ".shtml", ".mht", ".mhtml", ".svgz", ".php", ".asp", ".aspx", ".jsp",
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedExtensions = new()
    {
        ["identity-docs"] = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" },
        ["pay-stubs"] = new(StringComparer.OrdinalIgnoreCase) { ".pdf" },
        ["tax-documents"] = new(StringComparer.OrdinalIgnoreCase) { ".pdf" },
    };

    public const long MaxUploadBytes = 100L * 1024 * 1024;

    public static bool IsAllowedFileName(string entityType, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var extension = Path.GetExtension(fileName.Trim().TrimEnd('.', ' '));
        if (BlockedExtensions.Contains(extension)) return false;
        return !AllowedExtensions.TryGetValue(entityType, out var allowed) || allowed.Contains(extension);
    }

    public static string DisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        return name.Length == 0 ? "file" : name;
    }

    public static string ObjectKeyName(string fileName)
    {
        var name = Regex.Replace(DisplayName(fileName), "[^A-Za-z0-9._-]", "_").Trim('.');
        if (name.Length > 100) name = name[^100..];
        return name.Length == 0 ? "file" : name;
    }

    public static string NormalizeContentType(string? contentType)
        => MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.MediaType.HasValue
            ? parsed.MediaType.Value!.ToLowerInvariant()
            : "application/octet-stream";

    public static string ResolveBucket(string entityType, MinioOptions opts) => entityType switch
    {
        "expenses" => opts.ReceiptsBucket,
        "employees" or "identity-docs" or "employee-docs"
            or "compliance-templates" or "pay-stubs" or "tax-documents" => opts.EmployeeDocsBucket,
        // Sales- and purchasing-document attachments live with the general job
        // files; object keys are prefixed by entity type so there are no collisions.
        "sales-orders" or "customers" or "quotes" or "purchase-orders" => opts.JobFilesBucket,
        _ => opts.JobFilesBucket,
    };
}
