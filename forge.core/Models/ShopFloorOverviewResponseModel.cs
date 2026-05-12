namespace Forge.Core.Models;

public record ShopFloorOverviewResponseModel(
    List<ShopFloorJobResponseModel> ActiveJobs,
    List<ShopFloorWorkerResponseModel> Workers,
    int CompletedToday,
    int MaintenanceAlerts);
