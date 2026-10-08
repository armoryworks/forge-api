namespace Forge.Api.Features.Quality;

/// <summary>
/// Thrown when a stock-out is refused because the only stock that could cover it is on quality hold. Callers
/// that tolerate a plain stock shortfall, such as shipping, let this one through so the refusal reaches the user.
/// </summary>
public class QualityHoldException(string message) : InvalidOperationException(message);
