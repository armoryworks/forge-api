using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Writes system-wide audit_log_entries rows for cross-entity events that
/// don't naturally flow through SaveChangesAsync EF tracking — login,
/// logout, MFA setup/disable, password change, role assignment, period
/// lock/unlock, system-config edits, etc.
///
/// Per-entity events (CustomerCreated, PartUpdated, etc.) are written
/// automatically by AppDbContext.CaptureAuditEntries; this writer covers
/// what that loop cannot see. Phase 3 / WU-03 / A1.
/// </summary>
public interface ISystemAuditWriter
{
    /// <summary>
    /// Records a system-wide audit entry. Persists immediately via
    /// SaveChangesAsync — caller does not need to call SaveChanges separately.
    /// </summary>
    /// <param name="action">Short verb like "UserLoggedIn", "RoleAssigned".</param>
    /// <param name="userId">Actor's user id (0 for anonymous / unknown).</param>
    /// <param name="entityType">Optional entity class name when the audit
    /// row is anchored to a specific entity (e.g. "ApplicationUser" for
    /// MFA setup on a particular user).</param>
    /// <param name="entityId">Optional entity id matching entityType.</param>
    /// <param name="details">Free-form JSON-encoded payload (event-specific).</param>
    Task WriteAsync(
        string action,
        int userId,
        string? entityType = null,
        int? entityId = null,
        string? details = null,
        CancellationToken ct = default);
}

public class SystemAuditWriter(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IClock clock) : ISystemAuditWriter
{
    public async Task WriteAsync(
        string action,
        int userId,
        string? entityType = null,
        int? entityId = null,
        string? details = null,
        CancellationToken ct = default)
    {
        var ctx = httpContext.HttpContext;
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var ua = ctx?.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(ua) && ua.Length > 500)
            ua = ua[..500];

        // Truncate action defensively (column max length 100).
        if (action.Length > 100) action = action[..100];
        if (entityType is { Length: > 50 } et) entityType = et[..50];

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ip,
            UserAgent = string.IsNullOrEmpty(ua) ? null : ua,
            CreatedAt = clock.UtcNow,
        });

        // Suppress the per-entity ActivityLog auto-capture for this AuditLogEntry
        // row itself — AuditLogEntry is already on the AuditExcludedTypes list,
        // so it is naturally skipped by CaptureAuditEntries.
        await db.SaveChangesAsync(ct);
    }
}
