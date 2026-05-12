namespace Forge.Core.Models;

public record AuditLogEntryResponseModel(
    int Id,
    int UserId,
    string UserName,
    string Action,
    string? EntityType,
    int? EntityId,
    string? Details,
    string? IpAddress,
    DateTimeOffset CreatedAt);
