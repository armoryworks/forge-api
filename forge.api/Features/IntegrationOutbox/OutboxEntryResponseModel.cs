using Forge.Core.Enums;

namespace Forge.Api.Features.IntegrationOutbox;

public record OutboxEntryResponseModel(
    int Id,
    IntegrationProvider Provider,
    string OperationKey,
    OutboxStatus Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? SentAt,
    string? LastError,
    string? EntityType,
    int? EntityId,
    DateTimeOffset CreatedAt);
