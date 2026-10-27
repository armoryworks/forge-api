namespace Forge.Api.Authorization;

/// <summary>
/// The roles of <see cref="Forge.Api.Controllers.InventoryController"/>. <see cref="Edit"/> may move, receive and
/// count stock; <see cref="Read"/> adds the Production Planner, who reads stock, ATP and locations only, so every
/// write action of the controller carries <see cref="Edit"/> or a narrower set.
/// </summary>
public static class InventoryRoles
{
    public const string Edit = "Admin,Manager,OfficeManager,Engineer,ProductionWorker";

    public const string Read = Edit + "," + PlanningRoles.Planner;
}
