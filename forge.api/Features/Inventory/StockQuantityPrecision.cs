using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public static class StockQuantityPrecision
{
    public static async Task EnsureFitsStockUnitAsync(
        AppDbContext db,
        int? partId,
        IEnumerable<(string Field, string Label, decimal? Value)> quantities,
        CancellationToken ct = default)
    {
        if (partId is not int id)
            return;

        var unit = await db.Parts
            .AsNoTracking()
            .Where(p => p.Id == id && p.StockUom != null)
            .Select(p => new { p.StockUom!.Code, p.StockUom.DecimalPlaces })
            .FirstOrDefaultAsync(ct);
        if (unit is null)
            return;

        var failures = quantities
            .Where(q => q.Value is decimal value && decimal.Round(value, unit.DecimalPlaces) != value)
            .Select(q => new ValidationFailure(q.Field, unit.DecimalPlaces == 0
                ? $"{q.Label} must be a whole number of {unit.Code}."
                : $"{q.Label} can have at most {unit.DecimalPlaces} decimal places ({unit.Code})."))
            .ToList();
        if (failures.Count > 0)
            throw new ValidationException(failures);
    }
}
