using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record UpdatePayPeriodSettingsCommand(string Type, DateTimeOffset? AnchorDate) : IRequest;

public class UpdatePayPeriodSettingsValidator : AbstractValidator<UpdatePayPeriodSettingsCommand>
{
    public UpdatePayPeriodSettingsValidator()
    {
        RuleFor(x => x.Type)
            .Must(t => t is "weekly" or "biweekly" or "semimonthly" or "monthly")
            .WithMessage("Type must be weekly, biweekly, semimonthly, or monthly.");
    }
}

public class UpdatePayPeriodSettingsHandler(AppDbContext db) : IRequestHandler<UpdatePayPeriodSettingsCommand>
{
    public async Task Handle(UpdatePayPeriodSettingsCommand request, CancellationToken ct)
    {
        await UpsertSetting("pay_period_type", request.Type, "Pay period type: weekly, biweekly, semimonthly, monthly", ct);

        if (request.AnchorDate.HasValue)
            await UpsertSetting("pay_period_anchor", request.AnchorDate.Value.ToString("O"),
                "Pay period anchor date for offset calculation", ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertSetting(string key, string value, string description, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
            await db.SystemSettings.AddAsync(new SystemSetting { Key = key, Value = value, Description = description }, ct);
        else
            setting.Value = value;
    }
}
