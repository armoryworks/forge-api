namespace Forge.Api.Authorization;

/// <summary>
/// The roles that may read work orders. <see cref="Read"/> gates
/// <see cref="Forge.Api.Controllers.JobsController"/> and joining a <see cref="Forge.Api.Hubs.BoardHub"/> job
/// group, so the live <c>JobDetail</c> pushes reach exactly the roles that could fetch the same payload over
/// REST. <see cref="BoardRead"/> gates <see cref="Forge.Api.Controllers.JobListController"/> and joining a board
/// group, so every role that can load the board also gets its live updates. <see cref="StockMove"/> gates the job
/// actions that move stock and post to the ledger (material issues and returns, receiving a production run to
/// stock): it is <see cref="Read"/> without the Production Planner, who plans jobs but does not transact stock.
/// </summary>
public static class JobRoles
{
    public const string Read = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager," + PlanningRoles.Planner;

    public const string BoardRead = Read + ",Procurement";

    public const string StockMove = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager";
}
