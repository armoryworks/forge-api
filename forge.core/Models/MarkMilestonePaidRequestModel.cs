namespace Forge.Core.Models;

/// <summary>
/// POST body for recording a (possibly partial) payment against a milestone.
/// Payments accumulate; the milestone flips to Paid once Σ paid covers the
/// derived (or locked) amount.
/// </summary>
public record MarkMilestonePaidRequestModel(decimal PaidAmount, string? PaidReference = null);
