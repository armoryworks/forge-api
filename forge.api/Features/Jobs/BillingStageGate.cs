using System.Security.Claims;

using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.Jobs;

public static class BillingStageGate
{
    public static readonly IReadOnlyList<string> Roles = ["Admin", "Manager", "OfficeManager"];

    public static bool IsBillingStage(AccountingDocumentType? documentType) =>
        documentType is AccountingDocumentType.Invoice or AccountingDocumentType.Payment;

    public static bool MayEnter(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated != true || Roles.Any(user.IsInRole);

    public static void EnsureMayEnter(JobStage target, ClaimsPrincipal? user)
    {
        if (IsBillingStage(target.AccountingDocumentType) && !MayEnter(user))
            throw new ForbiddenException(
                $"Moving a work order to {target.Name} bills the customer. Ask the office to move it.");
    }
}
