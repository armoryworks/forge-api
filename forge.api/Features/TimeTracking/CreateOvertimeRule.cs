using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record CreateOvertimeRuleCommand(CreateOvertimeRuleRequestModel Request) : IRequest<OvertimeRuleResponseModel>;

public class CreateOvertimeRuleValidator : AbstractValidator<CreateOvertimeRuleCommand>
{
    public CreateOvertimeRuleValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.DailyThresholdHours).GreaterThan(0);
        RuleFor(x => x.Request.WeeklyThresholdHours).GreaterThan(0);
        RuleFor(x => x.Request.OvertimeMultiplier).GreaterThan(0);
        RuleFor(x => x.Request.DoubletimeMultiplier).GreaterThan(0);
    }
}

public class CreateOvertimeRuleHandler(AppDbContext db) : IRequestHandler<CreateOvertimeRuleCommand, OvertimeRuleResponseModel>
{
    public async Task<OvertimeRuleResponseModel> Handle(CreateOvertimeRuleCommand request, CancellationToken cancellationToken)
    {
        // If setting as default, clear existing default
        if (request.Request.IsDefault)
        {
            var existingDefault = await db.Set<OvertimeRule>()
                .Where(r => r.IsDefault && r.DeletedAt == null)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingDefault != null)
                existingDefault.IsDefault = false;
        }

        var rule = new OvertimeRule
        {
            Name = request.Request.Name.Trim(),
            DailyThresholdHours = request.Request.DailyThresholdHours,
            WeeklyThresholdHours = request.Request.WeeklyThresholdHours,
            OvertimeMultiplier = request.Request.OvertimeMultiplier,
            DoubletimeThresholdDailyHours = request.Request.DoubletimeThresholdDailyHours,
            DoubletimeThresholdWeeklyHours = request.Request.DoubletimeThresholdWeeklyHours,
            DoubletimeMultiplier = request.Request.DoubletimeMultiplier,
            IsDefault = request.Request.IsDefault,
            ApplyDailyBeforeWeekly = request.Request.ApplyDailyBeforeWeekly,
        };

        db.Set<OvertimeRule>().Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        return new OvertimeRuleResponseModel(
            rule.Id, rule.Name,
            rule.DailyThresholdHours, rule.WeeklyThresholdHours,
            rule.OvertimeMultiplier,
            rule.DoubletimeThresholdDailyHours, rule.DoubletimeThresholdWeeklyHours,
            rule.DoubletimeMultiplier,
            rule.IsDefault, rule.ApplyDailyBeforeWeekly);
    }
}
