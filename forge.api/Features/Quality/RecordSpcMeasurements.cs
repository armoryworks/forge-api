using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record RecordSpcMeasurementsCommand(RecordSpcMeasurementRequestModel Data)
    : IRequest<List<SpcMeasurementResponseModel>>;

public class RecordSpcMeasurementsCommandValidator : AbstractValidator<RecordSpcMeasurementsCommand>
{
    public RecordSpcMeasurementsCommandValidator()
    {
        RuleFor(x => x.Data.CharacteristicId).GreaterThan(0);
        RuleFor(x => x.Data.Subgroups).NotEmpty().WithMessage("At least one subgroup is required.");
        RuleForEach(x => x.Data.Subgroups).ChildRules(sg =>
        {
            sg.RuleFor(s => s.Values).NotEmpty().WithMessage("Each subgroup must have at least one value.");
            sg.RuleFor(s => s.Notes).MaximumLength(2000);
        });
    }
}

public class RecordSpcMeasurementsHandler(AppDbContext db, ISpcService spcService, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<RecordSpcMeasurementsCommand, List<SpcMeasurementResponseModel>>
{
    public async Task<List<SpcMeasurementResponseModel>> Handle(
        RecordSpcMeasurementsCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var characteristic = await db.SpcCharacteristics.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == data.CharacteristicId, cancellationToken)
            ?? throw new KeyNotFoundException($"SPC Characteristic {data.CharacteristicId} not found.");

        // Get next subgroup number
        var lastSubgroup = await db.SpcMeasurements
            .Where(m => m.CharacteristicId == data.CharacteristicId)
            .MaxAsync(m => (int?)m.SubgroupNumber, cancellationToken) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var measurements = new List<SpcMeasurement>();

        foreach (var subgroup in data.Subgroups)
        {
            lastSubgroup++;
            var values = subgroup.Values;
            var sortedValues = values.OrderBy(v => v).ToArray();

            var mean = values.Average();
            var range = values.Max() - values.Min();
            var median = sortedValues.Length % 2 == 0
                ? (sortedValues[sortedValues.Length / 2 - 1] + sortedValues[sortedValues.Length / 2]) / 2m
                : sortedValues[sortedValues.Length / 2];
            var stdDev = values.Length > 1
                ? (decimal)Math.Sqrt((double)values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1))
                : 0m;

            var isOutOfSpec = mean > characteristic.UpperSpecLimit || mean < characteristic.LowerSpecLimit
                || values.Any(v => v > characteristic.UpperSpecLimit || v < characteristic.LowerSpecLimit);

            var measurement = new SpcMeasurement
            {
                CharacteristicId = data.CharacteristicId,
                JobId = data.JobId,
                ProductionRunId = data.ProductionRunId,
                LotNumber = data.LotNumber?.Trim(),
                MeasuredById = userId,
                MeasuredAt = now,
                SubgroupNumber = lastSubgroup,
                ValuesJson = JsonSerializer.Serialize(values),
                Mean = Math.Round(mean, 6),
                Range = Math.Round(range, 6),
                StdDev = Math.Round(stdDev, 6),
                Median = Math.Round(median, 6),
                IsOutOfSpec = isOutOfSpec,
                Notes = subgroup.Notes?.Trim(),
            };

            measurements.Add(measurement);
        }

        db.SpcMeasurements.AddRange(measurements);
        await db.SaveChangesAsync(cancellationToken);

        // Evaluate OOC rules if control limits exist
        var activeLimit = await db.SpcControlLimits.AsNoTracking()
            .FirstOrDefaultAsync(cl => cl.CharacteristicId == data.CharacteristicId && cl.IsActive, cancellationToken);

        if (activeLimit != null)
        {
            var recentMeasurements = await db.SpcMeasurements.AsNoTracking()
                .Where(m => m.CharacteristicId == data.CharacteristicId)
                .OrderByDescending(m => m.SubgroupNumber)
                .Take(10)
                .OrderBy(m => m.SubgroupNumber)
                .ToListAsync(cancellationToken);

            var oocEvents = spcService.EvaluateControlRules(characteristic, activeLimit, recentMeasurements);
            if (oocEvents.Count > 0)
            {
                db.SpcOocEvents.AddRange(oocEvents);

                // Mark measurements as OOC
                var oocMeasurementIds = oocEvents.Select(e => e.MeasurementId).ToHashSet();
                foreach (var m in measurements.Where(m => oocMeasurementIds.Contains(m.Id)))
                {
                    m.IsOutOfControl = true;
                    m.OocRuleViolated = oocEvents
                        .Where(e => e.MeasurementId == m.Id)
                        .Select(e => e.RuleName)
                        .FirstOrDefault();
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        // Resolve user name
        var userName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.LastName + ", " + u.FirstName)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        return measurements.Select(m => new SpcMeasurementResponseModel
        {
            Id = m.Id,
            CharacteristicId = m.CharacteristicId,
            JobId = m.JobId,
            ProductionRunId = m.ProductionRunId,
            LotNumber = m.LotNumber,
            MeasuredByName = userName,
            MeasuredAt = m.MeasuredAt,
            SubgroupNumber = m.SubgroupNumber,
            Values = JsonSerializer.Deserialize<decimal[]>(m.ValuesJson) ?? [],
            Mean = m.Mean,
            Range = m.Range,
            StdDev = m.StdDev,
            Median = m.Median,
            IsOutOfSpec = m.IsOutOfSpec,
            IsOutOfControl = m.IsOutOfControl,
            OocRuleViolated = m.OocRuleViolated,
            Notes = m.Notes,
        }).ToList();
    }
}
