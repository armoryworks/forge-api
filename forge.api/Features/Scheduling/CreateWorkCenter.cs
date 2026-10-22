using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Scheduling;

public record CreateWorkCenterCommand(
    string Name,
    string Code,
    string? Description,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent,
    int NumberOfMachines,
    decimal LaborCostPerHour,
    decimal BurdenRatePerHour,
    int? AssetId,
    int? CompanyLocationId,
    int SortOrder,
    int? TeamId = null,
    decimal? IdealCycleTimeSeconds = null,
    BurdenBasis? BurdenBasis = null) : IRequest<WorkCenterResponseModel>, IWorkCenterFields;

public class CreateWorkCenterValidator : AbstractValidator<CreateWorkCenterCommand>
{
    public CreateWorkCenterValidator()
    {
        Include(new WorkCenterFieldsValidator());
    }
}

public class CreateWorkCenterHandler(AppDbContext db) : IRequestHandler<CreateWorkCenterCommand, WorkCenterResponseModel>
{
    public async Task<WorkCenterResponseModel> Handle(CreateWorkCenterCommand request, CancellationToken cancellationToken)
    {
        var teamName = await WorkCenterTeamGuard.ResolveTeamNameAsync(db, request.TeamId, cancellationToken);
        var code = await WorkCenterCodeGuard.EnsureUniqueAsync(db, request.Code, null, cancellationToken);

        var burdenBasis = request.BurdenBasis
            ?? (request.AssetId is int assetId
                && await db.Assets.AnyAsync(a => a.Id == assetId && a.AssetType == AssetType.Machine, cancellationToken)
                ? BurdenBasis.Machine
                : BurdenBasis.Labor);

        var wc = new WorkCenter
        {
            Name = request.Name,
            Code = code,
            Description = request.Description,
            DailyCapacityHours = request.DailyCapacityHours,
            EfficiencyPercent = request.EfficiencyPercent,
            NumberOfMachines = request.NumberOfMachines,
            LaborCostPerHour = request.LaborCostPerHour,
            BurdenRatePerHour = request.BurdenRatePerHour,
            AssetId = request.AssetId,
            CompanyLocationId = request.CompanyLocationId,
            SortOrder = request.SortOrder,
            TeamId = request.TeamId,
            IdealCycleTimeSeconds = request.IdealCycleTimeSeconds,
            BurdenBasis = burdenBasis,
        };

        db.WorkCenters.Add(wc);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created",
            teamName is null ? $"Created work center {wc.Code}" : $"Created work center {wc.Code} owned by {teamName}",
            ("WorkCenter", wc.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new WorkCenterResponseModel(
            wc.Id, wc.Name, wc.Code, wc.Description,
            wc.DailyCapacityHours, wc.EfficiencyPercent,
            wc.NumberOfMachines, wc.LaborCostPerHour,
            wc.BurdenRatePerHour, wc.IsActive,
            wc.AssetId, null, wc.CompanyLocationId, null, wc.SortOrder,
            wc.TeamId, teamName, wc.IdealCycleTimeSeconds, wc.BurdenBasis);
    }
}
