namespace Forge.Core.Models.Communications;

/// <summary>Everything the review screen needs to decide whether to approve a draft.</summary>
public record CommunicationDetailResponseModel(
    int Id,
    string Channel,
    string Flow,
    string Subject,
    string? Body,
    string? FromAddress,
    DateTimeOffset OccurredAt,
    int? DurationMinutes,
    string? PartyType,
    int? PartyId,
    int? ContactId,
    string? ContactName,
    /// <summary>Exact | Domain | Unmatched. Only Exact may feed a draft order.</summary>
    string MatchConfidence,
    bool IsTriaged,
    int? HandledByUserId,
    IReadOnlyList<CommunicationArtifactResponseModel> Artifacts,
    IReadOnlyList<CommunicationLinkResponseModel> Links,
    /// <summary>Standing agreements on file for this party — the authorization chain.</summary>
    IReadOnlyList<PriorAgreementResponseModel> PriorAgreements,
    IReadOnlyList<ThreadMessageResponseModel> Thread);
