using FluentValidation;
using MediatR;

using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Opens a new costing period.</summary>
public record CreateCostingPeriodCommand(DateTime StartDate, DateTime EndDate)
    : IRequest<CostingPeriodResponseModel>;

public class CreateCostingPeriodValidator : AbstractValidator<CreateCostingPeriodCommand>
{
    public CreateCostingPeriodValidator()
    {
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate)
            .WithMessage("End date must be after start date.");
    }
}

public class CreateCostingPeriodHandler(AppDbContext db)
    : IRequestHandler<CreateCostingPeriodCommand, CostingPeriodResponseModel>
{
    public async Task<CostingPeriodResponseModel> Handle(CreateCostingPeriodCommand request, CancellationToken ct)
    {
        var period = new CostingPeriod
        {
            StartDate = DateTime.SpecifyKind(request.StartDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(request.EndDate, DateTimeKind.Utc),
            Status = CostingPeriodStatus.Open,
        };
        db.CostingPeriods.Add(period);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created", $"Opened costing period {period.StartDate:MM/dd/yyyy}–{period.EndDate:MM/dd/yyyy}",
            ("CostingPeriod", period.Id));
        await db.SaveChangesAsync(ct);

        return new CostingPeriodResponseModel(
            period.Id, period.StartDate, period.EndDate, period.Status.ToString(), period.FrozenAt, period.ClosedAt);
    }
}
