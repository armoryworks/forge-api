namespace Forge.Api.Authorization;

/// <summary>
/// The roles of <see cref="Forge.Api.Controllers.PartsController"/>. <see cref="Edit"/> may change parts, BOMs,
/// routings and prices; <see cref="Read"/> adds the Production Planner, who reads parts, BOMs and routings only, so
/// every write action and the price list of the controller carry <see cref="Edit"/> or a narrower set.
/// </summary>
public static class PartRoles
{
    public const string Edit = "Admin,Manager,Engineer,ProductionWorker,PM,OfficeManager";

    public const string Read = Edit + "," + PlanningRoles.Planner;
}
