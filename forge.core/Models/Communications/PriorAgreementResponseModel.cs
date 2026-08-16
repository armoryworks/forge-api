namespace Forge.Core.Models.Communications;

/// <summary>
/// A standing agreement the party already has on file — the chain a purchase
/// order leans on. Party-scoped, so it carries no sales order.
/// </summary>
public record PriorAgreementResponseModel(
    int Id,
    string StatementType,
    string Method,
    DateTimeOffset? CapturedAt,
    string? Sha256,
    string? Filename,
    string? Note);
