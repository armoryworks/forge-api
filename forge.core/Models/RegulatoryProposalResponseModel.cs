namespace Forge.Core.Models;

/// <summary>regulatory-watchtower: a proposed regulatory change awaiting admin review.</summary>
public record RegulatoryProposalResponseModel(
    int Id,
    int RegulatorySourceId,
    string SourceName,
    string Title,
    string? SummaryUrl,
    string? Details,
    string Status,
    int? TargetEventTypeId,
    DateTimeOffset CreatedAt);
