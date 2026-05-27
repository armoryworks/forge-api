namespace Forge.Core.Models;

/// <summary>C2: one inbound row for customer bulk-intake. ExternalRowKey lets the UI
/// match preview results back to its source rows.</summary>
public record BulkCustomerIntakeRow(
    string? ExternalRowKey,
    string Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? Notes);
