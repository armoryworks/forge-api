using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateJobOperationProgressRequestModel(
    decimal? CompletedQuantity,
    decimal? ScrapQuantity,
    JobOperationStatus? Status,
    uint? ExpectedVersion = null,
    decimal? ReworkQuantity = null,
    string? ReasonCode = null);
