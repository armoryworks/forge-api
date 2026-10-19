using System.Security.Claims;

using Forge.Core.Entities;

namespace Forge.Api.Authorization;

/// <summary>
/// Answers whether a caller may reach one record through the cross-cutting files,
/// activity and preview endpoints, using <see cref="EntityAccessRules"/>.
/// </summary>
public interface IEntityAccessResolver
{
    /// <summary>True when the caller may read (or, with <paramref name="forWrite"/>, change) the record's attachments, notes or preview.</summary>
    Task<bool> CanAccessAsync(string entityType, int entityId, ClaimsPrincipal? user, bool forWrite, CancellationToken ct);

    /// <summary>
    /// True when the caller may download (or, with <paramref name="forWrite"/>, delete) the file. On top of the
    /// entity rule, the uploader of an owned record's file and the user a pay stub, tax document or identity
    /// document belongs to keep access, and a file's <see cref="FileAttachment.RequiredRole"/> still applies.
    /// </summary>
    Task<bool> CanAccessFileAsync(FileAttachment file, ClaimsPrincipal? user, bool forWrite, CancellationToken ct);
}
