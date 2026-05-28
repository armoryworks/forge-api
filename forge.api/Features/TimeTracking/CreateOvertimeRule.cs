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

        if (request.Request.IsDefault)
        {
            // Atomic default swap. The filtered unique index (is_default = true AND
            // deleted_at IS NULL) can be violated by a single batched "clear old + insert
            // new default" SaveChanges if EF orders the INSERT before the clear UPDATE
            // (F-14-BE-02). Clear the prior default via a discrete ExecuteUpdate statement
            // first, then insert, both inside one transaction.
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Set<OvertimeRule>()
                .Where(r => r.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDefault, false), cancellationToken);

            db.Set<OvertimeRule>().Add(rule);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        else
        {
            db.Set<OvertimeRule>().Add(rule);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new OvertimeRuleResponseModel(
            rule.Id, rule.Name,
            rule.DailyThresholdHours, rule.WeeklyThresholdHours,
            rule.OvertimeMultiplier,
            rule.DoubletimeThresholdDailyHours, rule.DoubletimeThresholdWeeklyHours,
            rule.DoubletimeMultiplier,
            rule.IsDefault, rule.ApplyDailyBeforeWeekly);
    }
}
