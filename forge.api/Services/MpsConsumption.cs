namespace Forge.Api.Services;

public static class MpsConsumption
{
    public static Dictionary<int, decimal> NetQuantities(
        IEnumerable<(int LineId, int PartId, decimal Quantity, DateTimeOffset DueDate)> mpsLines,
        IEnumerable<(int PartId, decimal Quantity, DateTimeOffset RequiredDate)> salesDemand)
    {
        var salesByPart = salesDemand
            .GroupBy(s => s.PartId)
            .ToDictionary(g => g.Key, g => g.Select(s => (Date: s.RequiredDate.UtcDateTime.Date, s.Quantity)).ToList());

        var net = new Dictionary<int, decimal>();
        foreach (var partLines in mpsLines.GroupBy(l => l.PartId))
        {
            var sales = salesByPart.GetValueOrDefault(partLines.Key) ?? [];
            DateTime? previousDue = null;

            foreach (var bucket in partLines.GroupBy(l => l.DueDate.UtcDateTime.Date).OrderBy(g => g.Key))
            {
                var bucketEnd = bucket.Key;
                var unconsumedSales = sales
                    .Where(s => s.Date <= bucketEnd && (previousDue is null || s.Date > previousDue))
                    .Sum(s => s.Quantity);

                foreach (var line in bucket.OrderBy(l => l.LineId))
                {
                    var consumed = Math.Min(line.Quantity, unconsumedSales);
                    unconsumedSales -= consumed;
                    net[line.LineId] = line.Quantity - consumed;
                }

                previousDue = bucketEnd;
            }
        }

        return net;
    }
}
