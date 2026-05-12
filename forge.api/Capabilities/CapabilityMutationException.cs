namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-C — Throw from a capability mutation handler when a
/// dependency / mutex / version-mismatch precondition fails. The
/// CapabilitiesController catches and renders the WU-02 envelope shape
/// expected by the Phase B SignalR + UI integration.
///
/// Why a typed exception rather than throwing a generic 409 / 412:
/// MediatR handlers are the natural place to evaluate the rules, but the
/// status code + envelope shape varies by failure type. The exception
/// carries the structured payload so the controller does not need to
/// duplicate the rule evaluation.
/// </summary>
public sealed class CapabilityMutationException : Exception
{
    public CapabilityMutationException(int statusCode, string code, string message, IReadOnlyDictionary<string, object?>? extra = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = code;
        Extra = extra ?? new Dictionary<string, object?>();
    }

    public int StatusCode { get; }

    public string ErrorCode { get; }

    public IReadOnlyDictionary<string, object?> Extra { get; }

    /// <summary>
    /// Renders the envelope shape used by Phase B (CapabilityGateMiddleware)
    /// so the UI's HttpErrorInterceptor handles 409 / 412 from this surface
    /// the same way it handles the 403 capability-disabled response.
    /// </summary>
    public object ToEnvelope()
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = ErrorCode,
            ["message"] = Message,
        };
        foreach (var kv in Extra)
            error[kv.Key] = kv.Value;
        return new { errors = new[] { error } };
    }
}
