using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
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

        // Exclude headless service identities (e.g. the lead-intake integration
        // principal) from the people roster — they are API/SSO principals, not
        // employees, and showing them confuses the headcount. Done DB-side
        // before count/paging so totals stay accurate.
        var serviceUserIds = (await userManager.GetUsersInRoleAsync(SeedData.LeadIntakeRoleName))
            .Select(u => u.Id)
            .ToList();
        if (serviceUserIds.Count > 0)
            users = users.Where(u => !serviceUserIds.Contains(u.Id));

        // Manager restriction: only see users in same team
        var restrictedToTeam = false;
        if (!request.CallerIsAdmin && request.CallerUserId.HasValue)
        {
            var callerTeamId = await db.Users
                .Where(u => u.Id == request.CallerUserId.Value)
                .Select(u => u.TeamId)
                .FirstOrDefaultAsync(cancellationToken);

            if (callerTeamId.HasValue)
            {
                users = users.Where(u => u.TeamId == callerTeamId);
                restrictedToTeam = true;
            }
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

        var userless = await UserlessProfilesAsync(qry, restrictedToTeam, cancellationToken);
        if (userless.Count > 0)
            return await MergedPageAsync(qry, fullyOrdered, userless, cancellationToken);

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

        var items = await ProjectUsersAsync(usersList, cancellationToken);

        return new PagedResponse<EmployeeListItemResponseModel>(
            items,
            totalCount,
            qry.EffectivePage,
            qry.EffectivePageSize);
    }

    private async Task<List<Core.Entities.EmployeeProfile>> UserlessProfilesAsync(
        EmployeeListQuery qry, bool restrictedToTeam, CancellationToken cancellationToken)
    {
        if (restrictedToTeam || qry.IsActive == false || qry.TeamId.HasValue || !string.IsNullOrWhiteSpace(qry.Role))
            return [];

        var profiles = db.EmployeeProfiles
            .AsNoTracking()
            .Where(p => p.UserId == null && p.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(qry.Q))
        {
            var term = qry.Q.Trim().ToLower();
            profiles = profiles.Where(p =>
                (p.FirstName != null && p.FirstName.ToLower().Contains(term)) ||
                (p.LastName != null && p.LastName.ToLower().Contains(term)) ||
                (p.WorkEmail != null && p.WorkEmail.ToLower().Contains(term)));
        }

        if (qry.DateFrom.HasValue)
            profiles = profiles.Where(p => p.CreatedAt >= qry.DateFrom.Value);

        if (qry.DateTo.HasValue)
            profiles = profiles.Where(p => p.CreatedAt <= qry.DateTo.Value);

        if (!string.IsNullOrWhiteSpace(qry.Department))
        {
            var dept = qry.Department.Trim();
            profiles = profiles.Where(p => p.Department == dept);
        }

        return await profiles.ToListAsync(cancellationToken);
    }

    private async Task<PagedResponse<EmployeeListItemResponseModel>> MergedPageAsync(
        EmployeeListQuery qry,
        IQueryable<ApplicationUser> orderedUsers,
        List<Core.Entities.EmployeeProfile> userless,
        CancellationToken cancellationToken)
    {
        var users = await orderedUsers.ToListAsync(cancellationToken);
        var userItems = await ProjectUsersAsync(users, cancellationToken);
        var updatedAt = users.ToDictionary(u => u.Id, u => u.UpdatedAt);

        var rows = userItems
            .Select(i => (Item: i, UpdatedAt: updatedAt[i.Id]))
            .Concat(userless.Select(p => (Item: ProjectUserless(p), UpdatedAt: p.UpdatedAt)))
            .ToList();

        var text = StringComparer.CurrentCultureIgnoreCase;
        var desc = qry.OrderDescending;
        var ordered = (qry.Sort ?? "").Trim().ToLowerInvariant() switch
        {
            "firstname" => desc ? rows.OrderByDescending(r => r.Item.FirstName, text) : rows.OrderBy(r => r.Item.FirstName, text),
            "lastname"  => desc ? rows.OrderByDescending(r => r.Item.LastName, text)  : rows.OrderBy(r => r.Item.LastName, text),
            "email"     => desc ? rows.OrderByDescending(r => r.Item.Email, text)     : rows.OrderBy(r => r.Item.Email, text),
            "isactive"  => desc ? rows.OrderByDescending(r => r.Item.IsActive)        : rows.OrderBy(r => r.Item.IsActive),
            "createdat" => desc ? rows.OrderByDescending(r => r.Item.CreatedAt)       : rows.OrderBy(r => r.Item.CreatedAt),
            "updatedat" => desc ? rows.OrderByDescending(r => r.UpdatedAt)            : rows.OrderBy(r => r.UpdatedAt),
            "id"        => desc ? rows.OrderByDescending(r => r.Item.Id)              : rows.OrderBy(r => r.Item.Id),
            _           => rows.OrderBy(r => r.Item.LastName, text).ThenBy(r => r.Item.FirstName, text),
        };

        var page = ordered
            .ThenBy(r => r.Item.Id)
            .ThenBy(r => r.Item.HasLogin)
            .Skip(qry.Skip)
            .Take(qry.EffectivePageSize)
            .Select(r => r.Item)
            .ToList();

        return new PagedResponse<EmployeeListItemResponseModel>(
            page,
            rows.Count,
            qry.EffectivePage,
            qry.EffectivePageSize);
    }

    private static EmployeeListItemResponseModel ProjectUserless(Core.Entities.EmployeeProfile profile)
        => new(
            profile.Id,
            profile.FirstName ?? string.Empty,
            profile.LastName ?? string.Empty,
            null,
            null,
            profile.WorkEmail ?? string.Empty,
            profile.PhoneNumber,
            string.Empty,
            null,
            null,
            true,
            profile.JobTitle,
            profile.Department,
            profile.StartDate,
            profile.CreatedAt,
            HasLogin: false);

    private async Task<List<EmployeeListItemResponseModel>> ProjectUsersAsync(
        List<ApplicationUser> usersList, CancellationToken cancellationToken)
    {
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

        return items;
    }
}
