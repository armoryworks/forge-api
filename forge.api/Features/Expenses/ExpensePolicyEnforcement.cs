using FluentValidation;
using FluentValidation.Results;

namespace Forge.Api.Features.Expenses;

internal static class ExpensePolicyEnforcement
{
    public static void Enforce(decimal amount, string? description, string? receiptFileId, ExpenseSettingsResponse settings)
    {
        var failures = new List<ValidationFailure>();

        if (settings.MaxAmount.HasValue && amount > settings.MaxAmount.Value)
            failures.Add(new ValidationFailure(nameof(amount),
                $"Amount exceeds the policy maximum of {settings.MaxAmount.Value:F2}."));

        if (settings.MinDescriptionLength > 0 && (description?.Trim().Length ?? 0) < settings.MinDescriptionLength)
            failures.Add(new ValidationFailure(nameof(description),
                $"Description must be at least {settings.MinDescriptionLength} characters."));

        if (settings.RequireReceipt && string.IsNullOrWhiteSpace(receiptFileId))
            failures.Add(new ValidationFailure(nameof(receiptFileId),
                "Policy requires a receipt attachment for every expense."));

        if (failures.Count > 0) throw new ValidationException(failures);
    }
}
