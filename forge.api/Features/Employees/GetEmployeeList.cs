using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Phase 3 F7-broad / WU-22 — paged employee-list query.
///
/// Replaces the previous (search, teamId, role, isActive, callerUserId,
/// callerIsAdmin) signature with the bound EmployeeListQuery model + the
/// caller context (manager-restriction). The controller continues to accept
/// the legacy query-param names so existing callers work unchanged.
/// </summary>
public record GetEmployeeListQuery(
    EmployeeListQuery Query,
    int? CallerUserId,
    bool CallerIsAdmin) : IRequest<PagedResponse<EmployeeListItemResponseModel>>;

public class GetEmployeeListHandler(AppDbContext db, UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetEmployeeListQuery, PagedResponse<EmployeeListItemResponseModel>>
{
    public async Task<PagedResponse<EmployeeListItemResponseModel>> Handle(
        GetEmployeeListQuery request, CancellationToken cancellationToken)
    {
        var qry = request.Query;
        var users = db.Users
            .Include(u => u.WorkLocation)
            .AsNoTracking()
            .AsQueryable();

        // Manager restriction: only see users in same team
        if (!request.CallerIsAdmin && request.CallerUserId.HasValue)
        {
            var callerTeamId = await db.Users
                .Where(u => u.Id == request.CallerUserId.Value)
                .Select(u => u.TeamId)
                .FirstOrDefaultAsync(cancellationToken);

            if (callerTeamId.HasValue)
                users = users.Where(u => u.TeamId == callerTeamId);
        }

        // — Filters (DB-side) —
        if (qry.IsActive.HasValue)
            users = users.Where(u => u.IsActive == qry.IsActive.Value);

        if (qry.TeamId.HasValue)
            users = users.Where(u => u.TeamId == qry.TeamId.Value);

        if (!string.IsNullOrWhiteSpace(qry.Q))
        {
            var term = qry.Q.Trim().ToLower();
            users = users.Where(u =>
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term) ||
                u.Email!.ToLower().Contains(term));
        }

        if (qry.DateFrom.HasValue)
            users = users.Where(u => u.CreatedAt >= qry.DateFrom.Value);

        if (qry.DateTo.HasValue)
            users = users.Where(u => u.CreatedAt <= qry.DateTo.Value);

        // Department filter must join EmployeeProfile.
        if (!string.IsNullOrWhiteSpace(qry.Department))
        {
            var dept = qry.Department.Trim();
            var deptUserIds = await db.EmployeeProfiles
                .AsNoTracking()
                .Where(p => p.UserId != null && p.Department == dept)
                .Select(p => p.UserId!.Value)
                .ToListAsync(cancellationToken);
            users = users.Where(u => deptUserIds.Contains(u.Id));
        }

        // — Sort (whitelist; default = lastName/firstName asc per legacy) —
        var sortKey = (qry.Sort ?? "").Trim().ToLowerInvariant();
        var desc = qry.OrderDescending;
        IOrderedQueryable<ApplicationUser> orderedUsers = sortKey switch
        {
            "firstname"   => desc ? users.OrderByDescending(u => u.FirstName) : users.OrderBy(u => u.FirstName),
            "lastname"    => desc ? users.OrderByDescending(u => u.LastName)  : users.OrderBy(u => u.LastName),
            "email"       => desc ? users.OrderByDescending(u => u.Email)     : users.OrderBy(u => u.Email),
            "isactive"    => desc ? users.OrderByDescending(u => u.IsActive)  : users.OrderBy(u => u.IsActive),
            "createdat"   => desc ? users.OrderByDescending(u => u.CreatedAt) : users.OrderBy(u => u.CreatedAt),
            "updatedat"   => desc ? users.OrderByDescending(u => u.UpdatedAt) : users.OrderBy(u => u.UpdatedAt),
            "id"          => desc ? users.OrderByDescending(u => u.Id)        : users.OrderBy(u => u.Id),
            _             => users.OrderBy(u => u.LastName).ThenBy(u => u.FirstName),
        };

        // Stable secondary sort by Id
        var fullyOrdered = orderedUsers.ThenBy(u => u.Id);

        // Role filter is post-fetch (UserManager.GetRolesAsync is per-user).
        // When no role filter applies, we can paginate at DB level. When a
        // role filter applies, we must materialize all matching candidates,
        // filter by role in memory, then paginate the filtered list.
        List<ApplicationUser> usersList;
        int totalCount;

        if (string.IsNullOrWhiteSpace(qry.Role))
        {
            // DB-side count + DB-side paging (fast path).
            totalCount = await fullyOrdered.CountAsync(cancellationToken);
            usersList = await fullyOrdered
                .Skip(qry.Skip)
                .Take(qry.EffectivePageSize)
                .ToListAsync(cancellationToken);
        }
        else
        {
            // Role filter — materialize the ordered candidate set, filter by
            // role in memory, then paginate. Acceptable because the employee
            // population is bounded (small relative to transactional tables).
            var candidates = await fullyOrdered.ToListAsync(cancellationToken);
            var filtered = new List<ApplicationUser>();
            foreach (var u in candidates)
            {
                var roles = await userManager.GetRolesAsync(u);
                if (roles.Contains(qry.Role))
                    filtered.Add(u);
            }
            totalCount = filtered.Count;
            usersList = filtered.Skip(qry.Skip).Take(qry.EffectivePageSize).ToList();
        }

        // Batch-load profiles for the page slice. Phase 3 / WU-19:
        // EmployeeProfile.UserId is nullable; filter to linked profiles only.
        var pageUserIds = usersList.Select(u => u.Id).ToList();
        var profiles = pageUserIds.Count > 0
            ? await db.EmployeeProfiles
                .AsNoTracking()
                .Where(p => p.UserId != null && pageUserIds.Contains(p.UserId.Value))
                .ToDictionaryAsync(p => p.UserId!.Value, cancellationToken)
            : new();

        // Batch-load team names for the page slice
        var teamIds = usersList.Where(u => u.TeamId.HasValue).Select(u => u.TeamId!.Value).Distinct().ToList();
        var teams = teamIds.Count > 0
            ? await db.ReferenceData
                .AsNoTracking()
                .Where(r => teamIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Label, cancellationToken)
            : new Dictionary<int, string>();

        var items = new List<EmployeeListItemResponseModel>();
        foreach (var user in usersList)
        {
            var roles = await userManager.GetRolesAsync(user);
            var primaryRole = roles.FirstOrDefault() ?? "Unknown";

            profiles.TryGetValue(user.Id, out var profile);
            string? teamName = null;
            if (user.TeamId.HasValue)
                teams.TryGetValue(user.TeamId.Value, out teamName);

            items.Add(new EmployeeListItemResponseModel(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Initials,
                user.AvatarColor,
                user.Email ?? string.Empty,
                profile?.PhoneNumber,
                primaryRole,
                teamName,
                user.TeamId,
                user.IsActive,
                profile?.JobTitle,
                profile?.Department,
                profile?.StartDate,
                user.CreatedAt));
        }

        return new PagedResponse<EmployeeListItemResponseModel>(
            items,
            totalCount,
            qry.EffectivePage,
            qry.EffectivePageSize);
    }
}
