namespace Forge.Api.Authorization;

/// <summary>
/// The roles of <see cref="Forge.Api.Controllers.SalesOrdersController"/>. <see cref="Edit"/> sells;
/// <see cref="Read"/> adds the Production Planner, who reads demand only (orders, lines, schedule), so every write
/// action and the billing reads of the controller carry <see cref="Edit"/> or a narrower set.
/// </summary>
public static class SalesOrderRoles
{
    public const string Edit = "Admin,Manager,OfficeManager,PM";

    public const string Read = Edit + "," + PlanningRoles.Planner;
}
