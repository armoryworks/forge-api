using System.Security.Claims;

namespace Forge.Api.Authorization;

/// <summary>
/// The roles whose screens subscribe to <see cref="Forge.Api.Hubs.AccountingHub"/>: the accounting suite
/// (Controller) and payables (Admin, Manager, OfficeManager, Procurement, Controller). Only connections from
/// these roles join the hub's <see cref="Group"/>, so the ledger change signal never reaches anyone else.
/// </summary>
public static class AccountingRoles
{
    public const string Group = "accounting";

    public static readonly IReadOnlyList<string> Subscribers =
        ["Controller", "Admin", "Manager", "OfficeManager", "Procurement"];

    public static bool IsSubscriber(ClaimsPrincipal? user)
        => user is not null && Subscribers.Any(user.IsInRole);
}
