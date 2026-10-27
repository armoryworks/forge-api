namespace Forge.Api.Authorization;

/// <summary>
/// The roles of <see cref="Forge.Api.Controllers.PurchaseOrdersController"/>. <see cref="Edit"/> buys;
/// <see cref="Read"/> adds the Production Planner, who reads open supply only, so every write action of the
/// controller carries <see cref="Edit"/> or a narrower set.
/// </summary>
public static class PurchaseOrderRoles
{
    public const string Edit = "Admin,Manager,OfficeManager,Procurement";

    public const string Read = Edit + "," + PlanningRoles.Planner;
}
