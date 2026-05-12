namespace Forge.Core.Models;

public record NotificationMessage(
    string Title,
    string Body,
    string? Severity = "info",
    string? EntityType = null,
    int? EntityId = null,
    string? ActionUrl = null);
