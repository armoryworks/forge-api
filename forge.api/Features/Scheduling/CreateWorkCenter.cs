using FluentValidation;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

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
    int SortOrder) : IRequest<WorkCenterResponseModel>;

public class CreateWorkCenterValidator : AbstractValidator<CreateWorkCenterCommand>
{
    public CreateWorkCenterValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DailyCapacityHours).GreaterThan(0);
        RuleFor(x => x.EfficiencyPercent).InclusiveBetween(1, 200);
        RuleFor(x => x.NumberOfMachines).GreaterThan(0);
    }
}

public class CreateWorkCenterHandler(AppDbContext db) : IRequestHandler<CreateWorkCenterCommand, WorkCenterResponseModel>
{
    public async Task<WorkCenterResponseModel> Handle(CreateWorkCenterCommand request, CancellationToken cancellationToken)
    {
        var wc = new WorkCenter
        {
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            DailyCapacityHours = request.DailyCapacityHours,
            EfficiencyPercent = request.EfficiencyPercent,
            NumberOfMachines = request.NumberOfMachines,
            LaborCostPerHour = request.LaborCostPerHour,
            BurdenRatePerHour = request.BurdenRatePerHour,
            AssetId = request.AssetId,
            CompanyLocationId = request.CompanyLocationId,
            SortOrder = request.SortOrder,
        };

        db.WorkCenters.Add(wc);
        await db.SaveChangesAsync(cancellationToken);

        return new WorkCenterResponseModel(
            wc.Id, wc.Name, wc.Code, wc.Description,
            wc.DailyCapacityHours, wc.EfficiencyPercent,
            wc.NumberOfMachines, wc.LaborCostPerHour,
            wc.BurdenRatePerHour, wc.IsActive,
            wc.AssetId, null, wc.CompanyLocationId, null, wc.SortOrder);
    }
}
