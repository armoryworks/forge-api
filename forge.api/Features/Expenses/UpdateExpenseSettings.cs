using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record UpdateExpenseSettingsCommand(
    bool AllowSelfApproval,
    decimal? AutoApproveThreshold,
    decimal? MaxAmount,
    bool RequireReceipt,
    int MinDescriptionLength) : IRequest;

public class UpdateExpenseSettingsValidator : AbstractValidator<UpdateExpenseSettingsCommand>
{
    public UpdateExpenseSettingsValidator()
    {
        RuleFor(x => x.AutoApproveThreshold)
            .GreaterThan(0).When(x => x.AutoApproveThreshold.HasValue)
            .WithMessage("Auto-approve threshold must be greater than 0.");
        RuleFor(x => x.MaxAmount)
            .GreaterThan(0).When(x => x.MaxAmount.HasValue)
            .WithMessage("Max amount must be greater than 0.");
        RuleFor(x => x.MinDescriptionLength)
            .InclusiveBetween(0, 500)
            .WithMessage("Minimum description length must be between 0 and 500.");
        RuleFor(x => x)
            .Must(x => !x.MaxAmount.HasValue || !x.AutoApproveThreshold.HasValue || x.AutoApproveThreshold <= x.MaxAmount)
            .WithMessage("Auto-approve threshold cannot exceed the max expense amount.");
    }
}

public class UpdateExpenseSettingsHandler(AppDbContext db) : IRequestHandler<UpdateExpenseSettingsCommand>
{
    public async Task Handle(UpdateExpenseSettingsCommand request, CancellationToken ct)
    {
        await UpsertSetting("expense_self_approval", request.AllowSelfApproval.ToString().ToLower(),
            "Allow users to approve their own expenses", ct);

        await UpsertSetting("expense_auto_approve_threshold",
            request.AutoApproveThreshold.HasValue ? request.AutoApproveThreshold.Value.ToString("F2") : string.Empty,
            "Expenses below this amount are auto-approved", ct);

        await UpsertSetting("expense_max_amount",
            request.MaxAmount.HasValue ? request.MaxAmount.Value.ToString("F2") : string.Empty,
            "Maximum allowed amount for a single expense (empty = no limit)", ct);

        await UpsertSetting("expense_require_receipt", request.RequireReceipt.ToString().ToLower(),
            "Require a receipt file upload for every expense", ct);

        await UpsertSetting("expense_min_description_length", request.MinDescriptionLength.ToString(),
            "Minimum number of characters required in the expense description", ct);

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
