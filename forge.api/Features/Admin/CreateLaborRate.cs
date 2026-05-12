using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record CreateLaborRateCommand(
    int UserId,
    decimal StandardRatePerHour,
    decimal OvertimeRatePerHour,
    decimal? DoubletimeRatePerHour,
    DateOnly EffectiveFrom,
    string? Notes) : IRequest<LaborRateResponseModel>;

public class CreateLaborRateValidator : AbstractValidator<CreateLaborRateCommand>
{
    public CreateLaborRateValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.StandardRatePerHour).GreaterThan(0);
        RuleFor(x => x.OvertimeRatePerHour).GreaterThan(0);
    }
}

public class CreateLaborRateHandler(AppDbContext db)
    : IRequestHandler<CreateLaborRateCommand, LaborRateResponseModel>
{
    public async Task<LaborRateResponseModel> Handle(
        CreateLaborRateCommand request, CancellationToken cancellationToken)
    {
        // Close the previous effective rate
        var previousRate = await db.LaborRates
            .Where(r => r.UserId == request.UserId && r.EffectiveTo == null)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousRate != null && previousRate.EffectiveFrom < request.EffectiveFrom)
        {
            previousRate.EffectiveTo = request.EffectiveFrom.AddDays(-1);
        }

        var rate = new LaborRate
        {
            UserId = request.UserId,
            StandardRatePerHour = request.StandardRatePerHour,
            OvertimeRatePerHour = request.OvertimeRatePerHour,
            DoubletimeRatePerHour = request.DoubletimeRatePerHour,
            EffectiveFrom = request.EffectiveFrom,
            Notes = request.Notes,
        };

        db.LaborRates.Add(rate);
        await db.SaveChangesAsync(cancellationToken);

        return new LaborRateResponseModel
        {
            Id = rate.Id,
            UserId = rate.UserId,
            StandardRatePerHour = rate.StandardRatePerHour,
            OvertimeRatePerHour = rate.OvertimeRatePerHour,
            DoubletimeRatePerHour = rate.DoubletimeRatePerHour,
            EffectiveFrom = rate.EffectiveFrom,
            EffectiveTo = rate.EffectiveTo,
            Notes = rate.Notes,
        };
    }
}
