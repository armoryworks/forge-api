namespace Forge.Api.Middleware;

/// <summary>
/// Thrown when a feature has no working engine in this install (only a mock exists and the install
/// runs real integrations). Mapped to HTTP 501 problem details by <see cref="ExceptionHandlingMiddleware"/>,
/// so callers get an honest "not available" instead of a server error or a made-up result.
/// </summary>
public sealed class FeatureNotAvailableException(string message) : Exception(message);
