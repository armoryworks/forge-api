namespace Forge.Core.Models;

/// <summary>
/// Read model for a single PartPrice history row. Surface for the part-pricing
/// cluster on Part detail. <see cref="EffectiveTo"/> is null on the most recent
/// open row; closed rows are immutable history.
/// </summary>
public record PartPriceResponseModel(
    int Id,
    int PartId,
    decimal UnitPrice,
    string Currency,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Notes,
    DateTimeOffset CreatedAt);

/// <summary>
/// Request body for posting a new effective-dated PartPrice row. The handler
/// closes out any prior open row by setting its EffectiveTo to the new row's
/// EffectiveFrom.
/// </summary>
public record AddPartPriceRequestModel(
    decimal UnitPrice,
    string? Currency,
    DateTimeOffset? EffectiveFrom,
    string? Notes);
