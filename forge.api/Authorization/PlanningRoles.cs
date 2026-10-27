namespace Forge.Api.Authorization;

/// <summary>
/// The production-planning roles. <see cref="Planner"/> is the Production Planner role, which plans the shop
/// without changing master data, money or people. <see cref="Plan"/> gates the planning engines (MRP and the
/// finite scheduler) and the reads they lean on (shifts, working calendars).
/// </summary>
public static class PlanningRoles
{
    public const string Planner = "Production Planner";

    public const string Plan = "Admin,Manager," + Planner;
}
