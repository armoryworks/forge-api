namespace Forge.Api.Middleware;

/// <summary>
/// Thrown by a handler when the caller is authenticated but not permitted to act on a
/// specific resource — e.g. deleting another user's record without a manager role.
/// Mapped to HTTP 403 by <see cref="ExceptionHandlingMiddleware"/>, with <see cref="Code"/>
/// in the problem body when one is given.
///
/// Distinct from <see cref="UnauthorizedAccessException"/> (→ 401, "not authenticated")
/// and from the role-based <c>[Authorize(Roles=…)]</c> edge gate (which 403s before the
/// handler runs). Use this for per-row ownership checks that need the entity loaded first.
/// </summary>
public sealed class ForbiddenException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
