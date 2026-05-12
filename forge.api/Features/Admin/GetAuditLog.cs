using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetAuditLogQuery(
    int? UserId,
    string? Action,
    string? EntityType,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 25) : IRequest<PaginatedResult<AuditLogEntryResponseModel>>;

public record PaginatedResult<T>(List<T> Data, int Page, int PageSize, int TotalCount, int TotalPages);

public class GetAuditLogHandler(AppDbContext db) : IRequestHandler<GetAuditLogQuery, PaginatedResult<AuditLogEntryResponseModel>>
{
    public async Task<PaginatedResult<AuditLogEntryResponseModel>> Handle(GetAuditLogQuery request, CancellationToken ct)
    {
        var query = db.AuditLogEntries.AsQueryable();

        if (request.UserId.HasValue)
            query = query.Where(a => a.UserId == request.UserId.Value);
        if (!string.IsNullOrEmpty(request.Action))
            query = query.Where(a => a.Action == request.Action);
        if (!string.IsNullOrEmpty(request.EntityType))
            query = query.Where(a => a.EntityType == request.EntityType);
        if (request.From.HasValue)
            query = query.Where(a => a.CreatedAt >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(a => a.CreatedAt <= request.To.Value);

        var totalCount = await query.CountAsync(ct);

        // Left-join to Users so audit rows for unknown/system actors (UserId=0)
        // and rows whose user has been hard-deleted still surface in the result.
        var entries = await (
            from a in query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
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

        var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);
        return new PaginatedResult<AuditLogEntryResponseModel>(entries, request.Page, request.PageSize, totalCount, totalPages);
    }
}
