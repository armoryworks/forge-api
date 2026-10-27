using FluentValidation;
using MediatR;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record PreviewPayPeriodsQuery(string Type, DateOnly? AnchorDate, int Upcoming = 3) : IRequest<List<PayPeriodPreviewResponseModel>>;

public class PreviewPayPeriodsValidator : AbstractValidator<PreviewPayPeriodsQuery>
{
    public PreviewPayPeriodsValidator()
    {
        RuleFor(x => x.Type)
            .Must(t => t is "weekly" or "biweekly" or "semimonthly" or "monthly")
            .WithMessage("Type must be weekly, biweekly, semimonthly, or monthly.");
        RuleFor(x => x.Upcoming).InclusiveBetween(1, 12);
    }
}

public class PreviewPayPeriodsHandler(AppDbContext db, IClock clock) : IRequestHandler<PreviewPayPeriodsQuery, List<PayPeriodPreviewResponseModel>>
{
    public async Task<List<PayPeriodPreviewResponseModel>> Handle(PreviewPayPeriodsQuery request, CancellationToken ct)
    {
        var saved = await PayPeriodCalendar.LoadAsync(db, ct);
        var calendar = new PayPeriodCalendar(request.Type, request.AnchorDate ?? saved.Anchor);
        var today = DateOnly.FromDateTime(ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, ct), clock.UtcNow));

        var period = calendar.PeriodContaining(today);
        var result = new List<PayPeriodPreviewResponseModel> { new(period.Start, period.End, true) };
        for (var i = 0; i < request.Upcoming; i++)
        {
            period = calendar.PeriodContaining(period.End.AddDays(1));
            result.Add(new PayPeriodPreviewResponseModel(period.Start, period.End, false));
        }
        return result;
    }
}
