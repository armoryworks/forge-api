using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record UpdateWorkCenterCommand(
    int Id,
    string Name,
    string Code,
    string? Description,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent,
    int NumberOfMachines,
    decimal LaborCostPerHour,
    decimal BurdenRatePerHour,
    bool IsActive,
    int? AssetId,
    int? CompanyLocationId,
    int SortOrder) : IRequest<WorkCenterResponseModel>;

public class UpdateWorkCenterHandler(AppDbContext db) : IRequestHandler<UpdateWorkCenterCommand, WorkCenterResponseModel>
{
    public async Task<WorkCenterResponseModel> Handle(UpdateWorkCenterCommand request, CancellationToken cancellationToken)
    {
        var wc = await db.WorkCenters.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Work center {request.Id} not found.");

        wc.Name = request.Name;
        wc.Code = request.Code;
        wc.Description = request.Description;
        wc.DailyCapacityHours = request.DailyCapacityHours;
        wc.EfficiencyPercent = request.EfficiencyPercent;
        wc.NumberOfMachines = request.NumberOfMachines;
        wc.LaborCostPerHour = request.LaborCostPerHour;
        wc.BurdenRatePerHour = request.BurdenRatePerHour;
        wc.IsActive = request.IsActive;
        wc.AssetId = request.AssetId;
        wc.CompanyLocationId = request.CompanyLocationId;
        wc.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(cancellationToken);

        return new WorkCenterResponseModel(
            wc.Id, wc.Name, wc.Code, wc.Description,
            wc.DailyCapacityHours, wc.EfficiencyPercent,
            wc.NumberOfMachines, wc.LaborCostPerHour,
            wc.BurdenRatePerHour, wc.IsActive,
            wc.AssetId, null, wc.CompanyLocationId, null, wc.SortOrder);
    }
}
