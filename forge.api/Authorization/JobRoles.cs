namespace Forge.Api.Authorization;

/// <summary>
/// The roles that may read work orders. <see cref="Forge.Api.Controllers.JobsController"/> gates its REST
/// endpoints on this set and <see cref="Forge.Api.Hubs.BoardHub"/> gates joining the board and job groups on
/// it, so the live <c>JobDetail</c> pushes reach exactly the roles that could fetch the same payload over REST.
/// </summary>
public static class JobRoles
{
    public const string Read = "Admin,Manager,PM,Engineer,ProductionWorker,OfficeManager";
}
