using System.Diagnostics.CodeAnalysis;

using Cronos;

namespace Forge.Api.Features.Reports;

public static class ReportScheduleCron
{
    public static bool IsValid(string? expression) => TryParse(expression, out _);

    public static DateTimeOffset? NextOccurrence(string expression, DateTimeOffset afterUtc, TimeZoneInfo zone) =>
        TryParse(expression, out var cron) ? cron.GetNextOccurrence(afterUtc, zone) : null;

    private static bool TryParse(string? expression, [NotNullWhen(true)] out CronExpression? cron)
    {
        cron = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        var trimmed = expression.Trim();
        var format = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        return CronExpression.TryParse(trimmed, format, out cron);
    }
}
