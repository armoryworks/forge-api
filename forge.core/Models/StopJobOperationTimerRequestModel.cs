namespace Forge.Core.Models;

public record StopJobOperationTimerRequestModel(
    string? Notes = null,
    decimal? GoodQuantity = null,
    decimal? ScrapQuantity = null,
    bool MarkComplete = false,
    string? ReasonCode = null,
    bool AcknowledgeGate = false,
    string? OverrideReason = null);
