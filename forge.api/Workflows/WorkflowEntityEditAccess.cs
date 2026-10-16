using System.Reflection;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Forge.Api.Controllers;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern — who may walk a guided workflow over an entity that already exists. A bound run
/// writes the saved record through the workflow field appliers, so the caller must hold the roles the
/// entity's own update endpoint requires. The roles are read from that endpoint's
/// <see cref="AuthorizeAttribute"/>s (controller and action, each of which must be satisfied), so the
/// workflow surface can never grant more than the record's edit surface does.
/// </summary>
public static class WorkflowEntityEditAccess
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string[]>> RoleSetsByEntityType =
        new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vendor"] = RoleSetsOf(typeof(VendorsController), nameof(VendorsController.UpdateVendor)),
            ["Part"] = RoleSetsOf(typeof(PartsController), nameof(PartsController.UpdatePart)),
            ["Customer"] = RoleSetsOf(typeof(CustomersController), nameof(CustomersController.UpdateCustomer)),
        };

    /// <summary>
    /// True when <paramref name="user"/> may edit an existing <paramref name="entityType"/> record;
    /// false for an entity type that has no edit endpoint mapped here.
    /// </summary>
    public static bool CanEdit(ClaimsPrincipal? user, string entityType)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (!RoleSetsByEntityType.TryGetValue(entityType, out var roleSets)) return false;
        return roleSets.All(roles => roles.Any(user.IsInRole));
    }

    private static IReadOnlyList<string[]> RoleSetsOf(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{controller.Name}.{action} not found.");
        return controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Where(a => !string.IsNullOrWhiteSpace(a.Roles))
            .Select(a => a.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }
}
