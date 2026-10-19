using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public static class TimeLockBoundary
{
    public const string SettingKey = "time_locked_through";

    public static async Task<DateOnly?> GetAsync(AppDbContext db, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == SettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    public static async Task EnsureOpenAsync(AppDbContext db, DateOnly date, CancellationToken ct)
    {
        var lockedThrough = await GetAsync(db, ct);
        if (lockedThrough.HasValue && date <= lockedThrough.Value)
            throw new InvalidOperationException(
                $"Time on or before {lockedThrough.Value:yyyy-MM-dd} is in a locked pay period and cannot be added or moved there.");
    }

    public static async Task<DateOnly> AdvanceAsync(AppDbContext db, DateOnly lockThrough, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        var current = setting is not null
            && DateOnly.TryParseExact(setting.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateOnly?)null;

        var boundary = current.HasValue && current.Value > lockThrough ? current.Value : lockThrough;
        var text = boundary.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (setting is null)
            db.SystemSettings.Add(new SystemSetting
            {
                Key = SettingKey,
                Value = text,
                Description = "Time entries dated on or before this day are in a locked pay period",
            });
        else
            setting.Value = text;

        return boundary;
    }
}
