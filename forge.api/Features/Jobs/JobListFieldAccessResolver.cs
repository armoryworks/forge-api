using System.Security.Claims;

using Forge.Api.Authorization;
using Forge.Api.Controllers;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

/// <summary>
/// Derives a caller's <see cref="JobListFieldAccess"/> from the role lists of the endpoints that own each
/// field group: customers from the customer picker, sales orders from the sales-order read, billing from
/// the invoice read, cost from the job cost summary. A request with no signed-in user is a system call and
/// sees everything, as <see cref="BillingStageGate.MayEnter"/> does.
/// </summary>
public static class JobListFieldAccessResolver
{
    public static JobListFieldAccess For(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return JobListFieldAccess.Full;
        return new JobListFieldAccess(
            Customer: InAny(user, CustomerDropdownController.ReadRoles),
            SalesOrder: InAny(user, SalesOrderRoles.Read),
            Billing: BillingStageGate.Roles.Any(user.IsInRole),
            Cost: InAny(user, JobCostSummaryController.ReadRoles));
    }

    private static bool InAny(ClaimsPrincipal user, string roles) =>
        roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(user.IsInRole);
}
