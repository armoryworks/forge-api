namespace Forge.Api.Features.Inventory;

/// <summary>
/// Thrown when material is issued to a job from a lot whose expiration date has passed.
/// </summary>
public class LotExpiredException(string message) : InvalidOperationException(message);
