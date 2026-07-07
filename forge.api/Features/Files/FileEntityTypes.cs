using Forge.Core.Models;

namespace Forge.Api.Features.Files;

/// <summary>
/// Single source of truth for which entity types accept file uploads and which
/// MinIO bucket each routes to. The single and chunked upload paths previously
/// carried duplicated copies of both — they drifted, and `sales-orders` missing
/// from the whitelist rejected every file on the SO Documents tab (external QA
/// report; the .png in the report was incidental).
/// </summary>
public static class FileEntityTypes
{
    public static readonly HashSet<string> Valid =
    [
        "jobs", "expenses", "assets", "parts", "leads", "employees",
        "identity-docs", "employee-docs", "compliance-templates",
        "pay-stubs", "tax-documents",
        "sales-orders", "customers", "quotes",
    ];

    public static string ResolveBucket(string entityType, MinioOptions opts) => entityType switch
    {
        "expenses" => opts.ReceiptsBucket,
        "employees" or "identity-docs" or "employee-docs"
            or "compliance-templates" or "pay-stubs" or "tax-documents" => opts.EmployeeDocsBucket,
        // Sales-document attachments live with the general job files; object
        // keys are prefixed by entity type so there are no collisions.
        "sales-orders" or "customers" or "quotes" => opts.JobFilesBucket,
        _ => opts.JobFilesBucket,
    };
}
