namespace Forge.Core.Models;

/// <summary>
/// PUT payload that sets the exact set of lots attached to a stage. Lots present
/// on the stage but absent from <see cref="LotIds"/> are detached
/// (<c>SalesOrderStageId = null</c>).
/// </summary>
public record AssignLotsToStageRequestModel(
    List<int> LotIds);
