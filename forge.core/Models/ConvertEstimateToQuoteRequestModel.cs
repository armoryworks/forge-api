namespace Forge.Core.Models;

/// <summary>
/// #24: optional per-line resolutions for the estimate → quote convert.
/// Null/empty keeps the legacy carry-everything-over behavior (backward
/// compatible with callers that POST an empty body).
/// </summary>
public record ConvertEstimateToQuoteRequestModel(
    IReadOnlyList<EstimateLineResolutionModel>? LineResolutions);
