using Forge.Core.Entities;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Takes a quantity out of a part's active bin contents at one location, row by row in the order given
/// (un-lotted first, then oldest lot), never touching a row's reserved units. A row drawn to zero is marked
/// removed. The caller checks that enough free stock exists before drawing.
/// </summary>
public static class BinContentDrawDown
{
    public static List<(BinContent Row, decimal Taken)> Take(
        IEnumerable<BinContent> rows, decimal quantity, int userId, DateTimeOffset now)
    {
        var drawn = new List<(BinContent Row, decimal Taken)>();
        var remaining = quantity;
        foreach (var row in rows)
        {
            if (remaining <= 0)
                break;
            var free = row.Quantity - row.ReservedQuantity;
            if (free <= 0)
                continue;

            var take = Math.Min(free, remaining);
            row.Quantity -= take;
            remaining -= take;
            if (row.Quantity == 0)
            {
                row.RemovedAt = now;
                row.RemovedBy = userId;
            }
            drawn.Add((row, take));
        }
        return drawn;
    }
}
