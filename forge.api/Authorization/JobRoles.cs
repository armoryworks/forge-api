namespace Forge.Api.Authorization;

/// <summary>
/// The roles that may read work orders. <see cref="Read"/> gates
/// <see cref="Forge.Api.Controllers.JobsController"/> and joining a <see cref="Forge.Api.Hubs.BoardHub"/> job
/// group, so the live <c>JobDetail</c> pushes reach exactly the roles that could fetch the same payload over
/// REST. <see cref="BoardRead"/> gates <see cref="Forge.Api.Controllers.JobListController"/> and joining a board
/// group, so every role that can load the board also gets its live updates.
/// </summary>
public static class JobRoles
{
    public const string Read = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager," + PlanningRoles.Planner;

    public const string BoardRead = Read + ",Procurement";
}
