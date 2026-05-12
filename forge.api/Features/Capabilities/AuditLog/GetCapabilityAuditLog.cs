using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.AuditLog;

/// <summary>
/// Phase 4 Phase-E — Returns audit-log entries scoped to a single capability,
/// driving the per-capability detail page's "Recent activity" section
/// (4E §Screen 5). The query reuses the global <c>audit_log_entries</c> table
/// filtered by <c>entity_type='Capability'</c> and the resolved capability id;
/// no separate audit table is needed.
///
/// Per 4E-decisions-log #8: scoped table on the detail page (vs link-out to
/// global history) — the UX win of inline situational awareness wins.
///
/// Pagination uses the simple cursor pattern: <c>?before=&lt;timestamp&gt;</c>
/// + <c>?take=N</c>. The page-level history surface (Screen 4) reuses the
/// existing <c>/api/v1/admin/audit-log</c> endpoint; this endpoint is
/// per-capability only.
/// </summary>
public record GetCapabilityAuditLogQuery(
    string Code,
    DateTimeOffset? Before,
    int Take) : IRequest<IReadOnlyList<AuditLogEntryResponseModel>>;

public class GetCapabilityAuditLogHandler(AppDbContext db)
    : IRequestHandler<GetCapabilityAuditLogQuery, IReadOnlyList<AuditLogEntryResponseModel>>
{
    public async Task<IReadOnlyList<AuditLogEntryResponseModel>> Handle(
        GetCapabilityAuditLogQuery request,
        CancellationToken ct)
    {
        // Resolve code → capability id (the audit rows store the integer Id,
        // per Phase A decision D2 — keeps entity_id an int across the table).
        var capability = await db.Capabilities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == request.Code, ct)
            ?? throw new KeyNotFoundException($"Capability '{request.Code}' not found.");

        var take = request.Take <= 0 ? 25 : Math.Min(request.Take, 200);

        var query = db.AuditLogEntries
            .AsNoTracking()
            .Where(a => a.EntityType == CapabilityAuditEvents.EntityType
                && a.EntityId == capability.Id);

        if (request.Before.HasValue)
            query = query.Where(a => a.CreatedAt < request.Before.Value);

        // Left-join to Users so audit rows for unknown/system actors (UserId=0)
        // and rows whose user has been hard-deleted still surface.
        var entries = await (
            from a in query.OrderByDescending(a => a.CreatedAt).Take(take)
            join u in db.Users on a.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new AuditLogEntryResponseModel(
                a.Id,
                a.UserId,
                u != null ? (u.FirstName + " " + u.LastName).Trim() : string.Empty,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Details,
                a.IpAddress,
                a.CreatedAt)
        ).ToListAsync(ct);

        return entries;
    }
}
