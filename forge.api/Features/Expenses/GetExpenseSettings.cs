using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record GetExpenseSettingsQuery : IRequest<ExpenseSettingsResponse>;

public record ExpenseSettingsResponse(
    bool AllowSelfApproval,
    decimal? AutoApproveThreshold,
    decimal? MaxAmount,
    bool RequireReceipt,
    int MinDescriptionLength);

public class GetExpenseSettingsHandler(AppDbContext db) : IRequestHandler<GetExpenseSettingsQuery, ExpenseSettingsResponse>
{
    public async Task<ExpenseSettingsResponse> Handle(GetExpenseSettingsQuery request, CancellationToken ct)
    {
        var settings = await db.SystemSettings
            .Where(s => s.Key.StartsWith("expense_"))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new ExpenseSettingsResponse(
            AllowSelfApproval: settings.GetValueOrDefault("expense_self_approval") == "true",
            AutoApproveThreshold: ParseDecimal(settings, "expense_auto_approve_threshold"),
            MaxAmount: ParseDecimal(settings, "expense_max_amount"),
            RequireReceipt: settings.GetValueOrDefault("expense_require_receipt") == "true",
            MinDescriptionLength: ParseInt(settings, "expense_min_description_length") ?? 0);
    }

    private static decimal? ParseDecimal(Dictionary<string, string> s, string key) =>
        s.TryGetValue(key, out var v) && decimal.TryParse(v, out var d) ? d : null;

    private static int? ParseInt(Dictionary<string, string> s, string key) =>
        s.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : null;
}
