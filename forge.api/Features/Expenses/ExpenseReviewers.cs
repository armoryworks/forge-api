using System.Security.Claims;

using Forge.Api.Authorization;

namespace Forge.Api.Features.Expenses;

internal static class ExpenseReviewers
{
    private static readonly string[] Roles = EntityAccessRules.HrRoles.Split(',');

    public static bool Includes(ClaimsPrincipal? user) => user is not null && Roles.Any(user.IsInRole);
}
