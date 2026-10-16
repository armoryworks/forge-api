using System.Reflection;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Forge.Api.Controllers;
using Forge.Api.Middleware;
using Forge.Core.Entities;

namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern — who may walk a guided workflow over an entity. An unbound run materializes a new
/// record, so the caller must hold the roles the entity's own create endpoint requires; a bound run
/// writes the saved record through the workflow field appliers, so the caller must hold the roles the
/// entity's own update endpoint requires. The roles are read from those endpoints'
/// <see cref="AuthorizeAttribute"/>s (controller and action, each of which must be satisfied), so the
/// workflow surface can never grant more than the record's own create and edit surfaces do.
/// </summary>
public static class WorkflowEntityEditAccess
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string[]>> EditRoleSetsByEntityType =
        new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vendor"] = RoleSetsOf(typeof(VendorsController), nameof(VendorsController.UpdateVendor)),
            ["Part"] = RoleSetsOf(typeof(PartsController), nameof(PartsController.UpdatePart)),
            ["Customer"] = RoleSetsOf(typeof(CustomersController), nameof(CustomersController.UpdateCustomer)),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string[]>> CreateRoleSetsByEntityType =
        new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vendor"] = RoleSetsOf(typeof(VendorsController), nameof(VendorsController.CreateVendor)),
            ["Part"] = RoleSetsOf(typeof(PartsController), nameof(PartsController.CreatePart)),
            ["Customer"] = RoleSetsOf(typeof(CustomersController), nameof(CustomersController.CreateCustomer)),
        };

    /// <summary>
    /// True when <paramref name="user"/> may edit an existing <paramref name="entityType"/> record;
    /// false for an entity type that has no edit endpoint mapped here.
    /// </summary>
    public static bool CanEdit(ClaimsPrincipal? user, string entityType) =>
        Satisfies(user, EditRoleSetsByEntityType, entityType);

    /// <summary>
    /// True when <paramref name="user"/> may create a new <paramref name="entityType"/> record;
    /// false for an entity type that has no create endpoint mapped here.
    /// </summary>
    public static bool CanCreate(ClaimsPrincipal? user, string entityType) =>
        Satisfies(user, CreateRoleSetsByEntityType, entityType);

    /// <summary>
    /// True when <paramref name="user"/> may read or drive <paramref name="run"/>: the user who started it,
    /// while they still hold the access the run needs (create while unbound, edit once bound), or anyone
    /// who may edit the run's entity type, so a manager can take the run over.
    /// </summary>
    public static bool CanAccessRun(ClaimsPrincipal? user, int? userId, WorkflowRun run)
    {
        if (CanEdit(user, run.EntityType)) return true;
        var isStarter = userId is int id && id != 0 && run.StartedByUserId == id;
        return isStarter && run.EntityId is null && CanCreate(user, run.EntityType);
    }

    /// <summary>
    /// Throws <see cref="ForbiddenException"/> unless <see cref="CanAccessRun"/> allows
    /// <paramref name="user"/> onto <paramref name="run"/>.
    /// </summary>
    public static void EnsureCanAccessRun(ClaimsPrincipal? user, int? userId, WorkflowRun run)
    {
        if (!CanAccessRun(user, userId, run))
            throw new ForbiddenException(
                $"You cannot open this {run.EntityType} workflow: it belongs to another user or your role cannot edit a {run.EntityType}.");
    }

    private static bool Satisfies(
        ClaimsPrincipal? user, IReadOnlyDictionary<string, IReadOnlyList<string[]>> roleSetsByEntityType, string entityType)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (!roleSetsByEntityType.TryGetValue(entityType, out var roleSets)) return false;
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
