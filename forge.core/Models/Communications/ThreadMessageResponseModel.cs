namespace Forge.Core.Models.Communications;

/// <summary>A sibling message in the same conversation. An audit trail made of disconnected fragments is not one.</summary>
public record ThreadMessageResponseModel(
    int Id,
    string Subject,
    string? FromAddress,
    DateTimeOffset OccurredAt,
    string Flow);
