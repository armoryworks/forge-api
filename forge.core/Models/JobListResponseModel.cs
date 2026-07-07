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
    string? ParentJobNumber = null);
