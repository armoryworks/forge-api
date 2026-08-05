namespace Forge.Core.Models;

public record JobListResponseModel(
    int Id,
    string JobNumber,
    string Title,
    string StageName,
    string StageColor,
    int? AssigneeId,
    string? AssigneeInitials,
    string? AssigneeColor,
    string PriorityName,
    DateTimeOffset? DueDate,
    bool IsOverdue,
    string? CustomerName,
    string? BillingStatus,
    string? Disposition,
    int ChildJobCount,
    string? ExternalRef,
    string? AccountingDocumentType,
    List<string> ActiveHolds,
    string? CoverPhotoUrl = null,
    int? ParentJobId = null,
    string? ParentJobNumber = null,
    // Card back-links: the customer and the originating sales order (via the
    // job's SO-line link) so the board can render navigable references.
    int? CustomerId = null,
    int? SalesOrderId = null,
    string? SalesOrderNumber = null);
