namespace Forge.Core.Models;

/// <summary>compliance-calendar A-4: workflow-status update payload for a tracking-tier event.</summary>
public record EventStatusRequestModel(
    string Status,
    int? OwnerUserId,
    string? WaivedReason,
    string? EvidenceUrl,
    int? EvidenceDocumentSetId);
