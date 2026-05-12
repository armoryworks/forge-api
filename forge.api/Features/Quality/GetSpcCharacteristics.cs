using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetSpcCharacteristicsQuery(int? PartId, int? OperationId, bool? IsActive) : IRequest<List<SpcCharacteristicResponseModel>>;

public class GetSpcCharacteristicsHandler(AppDbContext db)
    : IRequestHandler<GetSpcCharacteristicsQuery, List<SpcCharacteristicResponseModel>>
{
    public async Task<List<SpcCharacteristicResponseModel>> Handle(
        GetSpcCharacteristicsQuery request, CancellationToken cancellationToken)
    {
        var query = db.SpcCharacteristics
            .AsNoTracking()
            .Include(c => c.Part)
            .Include(c => c.Operation)
            .AsQueryable();

        if (request.PartId.HasValue)
            query = query.Where(c => c.PartId == request.PartId.Value);

        if (request.OperationId.HasValue)
            query = query.Where(c => c.OperationId == request.OperationId.Value);

        if (request.IsActive.HasValue)
            query = query.Where(c => c.IsActive == request.IsActive.Value);

        return await query
            .OrderBy(c => c.Part.PartNumber)
            .ThenBy(c => c.Name)
            .Select(c => new SpcCharacteristicResponseModel
            {
                Id = c.Id,
                PartId = c.PartId,
                PartNumber = c.Part.PartNumber,
                OperationId = c.OperationId,
                OperationName = c.Operation != null ? c.Operation.Title : null,
                Name = c.Name,
                Description = c.Description,
                MeasurementType = c.MeasurementType.ToString(),
                NominalValue = c.NominalValue,
                UpperSpecLimit = c.UpperSpecLimit,
                LowerSpecLimit = c.LowerSpecLimit,
                UnitOfMeasure = c.UnitOfMeasure,
                DecimalPlaces = c.DecimalPlaces,
                SampleSize = c.SampleSize,
                SampleFrequency = c.SampleFrequency,
                GageId = c.GageId,
                IsActive = c.IsActive,
                NotifyOnOoc = c.NotifyOnOoc,
                MeasurementCount = c.Measurements.Count(),
                LatestCpk = c.ControlLimits
                    .Where(cl => cl.IsActive)
                    .Select(cl => (decimal?)cl.Cpk)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
    }
}
