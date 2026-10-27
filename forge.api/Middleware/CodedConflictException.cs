namespace Forge.Api.Middleware;

/// <summary>
/// A business-rule refusal the client must tell apart from other refusals, e.g. <c>qbo-push-overlap</c>
/// (a re-push the caller may force) or <c>book-timezone-locked</c>. The exception middleware maps it to 409
/// with <see cref="Code"/> in the problem body. It derives from <see cref="InvalidOperationException"/> so
/// callers that catch the general refusal keep working.
/// </summary>
public class CodedConflictException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
