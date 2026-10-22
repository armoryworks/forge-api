namespace Forge.Api.Middleware;

/// <summary>
/// Thrown by a handler when a dependency it needs (e.g. the AI service) is down or not configured.
/// Mapped to HTTP 503 problem details by <see cref="ExceptionHandlingMiddleware"/>, so callers can
/// tell an outage apart from a real answer and retry later.
/// </summary>
public sealed class ServiceUnavailableException(string message) : Exception(message);
