using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>
/// Whether a gage may be used to take measurements. A gage that is out for calibration, out of service or
/// retired is refused, and so is one past its calibration due date. A gage that is due for calibration but not
/// yet past its due date is still usable.
/// </summary>
public static class GageUsability
{
    public static async Task EnsureUsableAsync(AppDbContext db, int gageId, DateOnly today, CancellationToken ct)
    {
        var gage = await db.Gages.AsNoTracking()
            .Where(g => g.Id == gageId)
            .Select(g => new { g.GageNumber, g.Status, g.NextCalibrationDue })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Gage {gageId} not found.");

        var reason = gage.Status switch
        {
            GageStatus.OutForCalibration => "is out for calibration",
            GageStatus.OutOfService => "is out of service",
            GageStatus.Retired => "is retired",
            _ when gage.NextCalibrationDue is DateOnly due && due < today => $"was due for calibration on {due:yyyy-MM-dd}",
            _ => null,
        };

        if (reason is not null)
            throw new InvalidOperationException(
                $"Gage {gage.GageNumber} {reason} and cannot be used for measurements. Use a gage that is in service and in calibration.");
    }
}
