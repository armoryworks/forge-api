using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetProcessCapabilityQuery(int CharacteristicId) : IRequest<SpcCapabilityReportModel>;

public class GetProcessCapabilityHandler(AppDbContext db, ISpcService spcService)
    : IRequestHandler<GetProcessCapabilityQuery, SpcCapabilityReportModel>
{
    public async Task<SpcCapabilityReportModel> Handle(
        GetProcessCapabilityQuery request, CancellationToken cancellationToken)
    {
        var characteristic = await db.SpcCharacteristics.AsNoTracking()
            .Include(c => c.Part)
            .FirstOrDefaultAsync(c => c.Id == request.CharacteristicId, cancellationToken)
            ?? throw new KeyNotFoundException($"SPC Characteristic {request.CharacteristicId} not found.");

        var measurements = await db.SpcMeasurements.AsNoTracking()
            .Where(m => m.CharacteristicId == request.CharacteristicId)
            .OrderBy(m => m.SubgroupNumber)
            .ToListAsync(cancellationToken);

        if (measurements.Count < 2)
        {
            return new SpcCapabilityReportModel
            {
                CharacteristicId = characteristic.Id,
                CharacteristicName = characteristic.Name,
                Usl = characteristic.UpperSpecLimit,
                Lsl = characteristic.LowerSpecLimit,
                Nominal = characteristic.NominalValue,
                SampleCount = measurements.Count,
                HistogramBuckets = [],
                NormalCurve = [],
            };
        }

        // Collect all individual values
        var allValues = measurements
            .SelectMany(m => JsonSerializer.Deserialize<decimal[]>(m.ValuesJson) ?? [])
            .ToList();

        var mean = allValues.Average();
        var sigma = (decimal)Math.Sqrt((double)allValues.Sum(v => (v - mean) * (v - mean)) / (allValues.Count - 1));

        var cp = spcService.CalculateCp(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, sigma);
        var cpk = spcService.CalculateCpk(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, mean, sigma);
        var pp = spcService.CalculatePp(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, sigma);
        var ppk = spcService.CalculatePpk(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, mean, sigma);

        // Build histogram (20 buckets across data range with padding)
        var dataMin = allValues.Min();
        var dataMax = allValues.Max();
        var padding = (dataMax - dataMin) * 0.1m;
        var histMin = Math.Min(dataMin - padding, characteristic.LowerSpecLimit - padding);
        var histMax = Math.Max(dataMax + padding, characteristic.UpperSpecLimit + padding);
        var bucketWidth = (histMax - histMin) / 20m;

        var buckets = new List<HistogramBucket>();
        for (int i = 0; i < 20; i++)
        {
            var from = histMin + i * bucketWidth;
            var to = from + bucketWidth;
            var count = allValues.Count(v => v >= from && (i == 19 ? v <= to : v < to));
            buckets.Add(new HistogramBucket(Math.Round(from, 6), Math.Round(to, 6), count));
        }

        // Build normal curve (50 points)
        var curvePoints = new List<NormalCurvePoint>();
        var doubleSigma = (double)sigma;
        var doubleMean = (double)mean;
        for (int i = 0; i < 50; i++)
        {
            var x = (double)histMin + i * (double)(histMax - histMin) / 49.0;
            var z = (x - doubleMean) / doubleSigma;
            var y = Math.Exp(-0.5 * z * z) / (doubleSigma * Math.Sqrt(2 * Math.PI));
            // Scale y to match histogram height
            var scaledY = y * (double)bucketWidth * allValues.Count;
            curvePoints.Add(new NormalCurvePoint(Math.Round((decimal)x, 6), Math.Round((decimal)scaledY, 4)));
        }

        return new SpcCapabilityReportModel
        {
            CharacteristicId = characteristic.Id,
            CharacteristicName = characteristic.Name,
            Usl = characteristic.UpperSpecLimit,
            Lsl = characteristic.LowerSpecLimit,
            Nominal = characteristic.NominalValue,
            Cp = cp,
            Cpk = cpk,
            Pp = pp,
            Ppk = ppk,
            Mean = Math.Round(mean, 6),
            Sigma = Math.Round(sigma, 6),
            SampleCount = allValues.Count,
            HistogramBuckets = buckets,
            NormalCurve = curvePoints,
        };
    }
}
