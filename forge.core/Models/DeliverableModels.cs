namespace Forge.Core.Models;

/// <summary>Request to create a new deliverable.</summary>
public record CreateDeliverableRequestModel(
    string Name,
    string? Description,
    int? JobId,
    int? ProjectId,
    int? CustomerId,
    int DeliverableTypeId,
    DateTimeOffset? DueDate,
    string? FileAttachmentIds,
    string? CloudLinkExternalId);

/// <summary>Request to update a deliverable's editable fields.</summary>
public record UpdateDeliverableRequestModel(
    string Name,
    string? Description,
    int? JobId,
    int? ProjectId,
    int? CustomerId,
    int DeliverableTypeId,
    string Status,
    DateTimeOffset? DueDate,
    string? FileAttachmentIds,
    string? CloudLinkExternalId);

/// <summary>Response shape for a deliverable.</summary>
public record DeliverableResponseModel(
    int Id,
    string Name,
    string? Description,
    int? JobId,
    int? ProjectId,
    int? CustomerId,
    int DeliverableTypeId,
    string Status,
    DateTimeOffset? DueDate,
    DateTimeOffset? DeliveredAt,
    int? DeliveredByUserId,
    string? FileAttachmentIds,
    string? CloudLinkExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
