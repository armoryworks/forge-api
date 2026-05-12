using Forge.Core.Enums;

namespace Forge.Core.Models;

public record EstimateListItemModel(
    int Id,
    int CustomerId,
    string CustomerName,
    string Title,
    decimal EstimatedAmount,
    string Status,
    DateTimeOffset? ValidUntil,
    int? GeneratedQuoteId,
    string? AssignedToName,
    DateTimeOffset CreatedAt);

public record EstimateDetailResponseModel(
    int Id,
    int CustomerId,
    string CustomerName,
    string Title,
    string? Description,
    decimal EstimatedAmount,
    string Status,
    DateTimeOffset? ValidUntil,
    string? Notes,
    int? AssignedToId,
    string? AssignedToName,
    int? GeneratedQuoteId,
    DateTimeOffset? ConvertedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateEstimateRequestModel(
    int CustomerId,
    string Title,
    string? Description,
    decimal EstimatedAmount,
    DateTimeOffset? ValidUntil,
    string? Notes,
    int? AssignedToId);

public record UpdateEstimateRequestModel(
    string? Title,
    string? Description,
    decimal? EstimatedAmount,
    QuoteStatus? Status,
    DateTimeOffset? ValidUntil,
    string? Notes,
    int? AssignedToId);
