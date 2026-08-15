namespace Forge.Core.Models;

public record AcceptChannelSettlementRequestModel
{
    /// <summary>Why the variance is being accepted. Required — an unexplained sign-off is worse than none.</summary>
    public string ResolutionNotes { get; init; } = string.Empty;
}
