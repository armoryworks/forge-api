using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetSpcMeasurementsQuery(
    int? CharacteristicId,
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    int? JobId) : IRequest<List<SpcMeasurementResponseModel>>;

public class GetSpcMeasurementsHandler(AppDbContext db)
    : IRequestHandler<GetSpcMeasurementsQuery, List<SpcMeasurementResponseModel>>
{
    public async Task<List<SpcMeasurementResponseModel>> Handle(
        GetSpcMeasurementsQuery request, CancellationToken cancellationToken)
    {
        var query = db.SpcMeasurements.AsNoTracking().AsQueryable();

        if (request.CharacteristicId.HasValue)
            query = query.Where(m => m.CharacteristicId == request.CharacteristicId.Value);

        if (request.DateFrom.HasValue)
            query = query.Where(m => m.MeasuredAt >= request.DateFrom.Value);

        if (request.DateTo.HasValue)
            query = query.Where(m => m.MeasuredAt <= request.DateTo.Value);

        if (request.JobId.HasValue)
            query = query.Where(m => m.JobId == request.JobId.Value);

        // Pre-fetch user names for measured_by
        var userIds = await query.Select(m => m.MeasuredById).Distinct().ToListAsync(cancellationToken);
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, cancellationToken);

        var measurements = await query
            .OrderByDescending(m => m.MeasuredAt)
            .ThenByDescending(m => m.SubgroupNumber)
            .Take(500)
            .ToListAsync(cancellationToken);

        return measurements.Select(m => new SpcMeasurementResponseModel
        {
            Id = m.Id,
            CharacteristicId = m.CharacteristicId,
            JobId = m.JobId,
            ProductionRunId = m.ProductionRunId,
            LotNumber = m.LotNumber,
            MeasuredByName = userNames.GetValueOrDefault(m.MeasuredById, ""),
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
