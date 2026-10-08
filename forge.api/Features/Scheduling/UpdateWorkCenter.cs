using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

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
    int SortOrder,
    int? TeamId = null) : IRequest<WorkCenterResponseModel>;

public class UpdateWorkCenterHandler(AppDbContext db) : IRequestHandler<UpdateWorkCenterCommand, WorkCenterResponseModel>
{
    public async Task<WorkCenterResponseModel> Handle(UpdateWorkCenterCommand request, CancellationToken cancellationToken)
    {
        var wc = await db.WorkCenters.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Work center {request.Id} not found.");

        var teamName = request.TeamId == wc.TeamId
            ? await db.Teams.Where(t => t.Id == wc.TeamId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken)
            : await WorkCenterTeamGuard.ResolveTeamNameAsync(db, request.TeamId, cancellationToken);

        var changedFields = new List<string>();
        if (request.Name != wc.Name) { wc.Name = request.Name; changedFields.Add("name"); }
        if (request.Code != wc.Code) { wc.Code = request.Code; changedFields.Add("code"); }
        if (request.Description != wc.Description) { wc.Description = request.Description; changedFields.Add("description"); }
        if (request.DailyCapacityHours != wc.DailyCapacityHours) { wc.DailyCapacityHours = request.DailyCapacityHours; changedFields.Add("dailyCapacityHours"); }
        if (request.EfficiencyPercent != wc.EfficiencyPercent) { wc.EfficiencyPercent = request.EfficiencyPercent; changedFields.Add("efficiencyPercent"); }
        if (request.NumberOfMachines != wc.NumberOfMachines) { wc.NumberOfMachines = request.NumberOfMachines; changedFields.Add("numberOfMachines"); }
        if (request.LaborCostPerHour != wc.LaborCostPerHour) { wc.LaborCostPerHour = request.LaborCostPerHour; changedFields.Add("laborCostPerHour"); }
        if (request.BurdenRatePerHour != wc.BurdenRatePerHour) { wc.BurdenRatePerHour = request.BurdenRatePerHour; changedFields.Add("burdenRatePerHour"); }
        if (request.IsActive != wc.IsActive) { wc.IsActive = request.IsActive; changedFields.Add("isActive"); }
        if (request.AssetId != wc.AssetId) { wc.AssetId = request.AssetId; changedFields.Add("assetId"); }
        if (request.CompanyLocationId != wc.CompanyLocationId) { wc.CompanyLocationId = request.CompanyLocationId; changedFields.Add("companyLocationId"); }
        if (request.SortOrder != wc.SortOrder) { wc.SortOrder = request.SortOrder; changedFields.Add("sortOrder"); }
        if (request.TeamId != wc.TeamId) { wc.TeamId = request.TeamId; changedFields.Add("teamId"); }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "updated",
                $"Updated work center {wc.Code} — {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("WorkCenter", wc.Id));
        }

        await db.SaveChangesAsync(cancellationToken);

        return new WorkCenterResponseModel(
            wc.Id, wc.Name, wc.Code, wc.Description,
            wc.DailyCapacityHours, wc.EfficiencyPercent,
            wc.NumberOfMachines, wc.LaborCostPerHour,
            wc.BurdenRatePerHour, wc.IsActive,
            wc.AssetId, null, wc.CompanyLocationId, null, wc.SortOrder,
            wc.TeamId, teamName);
    }
}
