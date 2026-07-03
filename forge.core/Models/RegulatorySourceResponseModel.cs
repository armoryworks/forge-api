namespace Forge.Core.Models;

/// <summary>regulatory-watchtower: a monitored regulatory source.</summary>
public record RegulatorySourceResponseModel(
    int Id,
    string Name,
    string? IssuingBody,
    string? Domain,
    string Url,
    string FeedType,
    string? IndustryGate,
    bool IsActive,
    DateTimeOffset? LastPolledAt);
