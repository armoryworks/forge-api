using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetGageCalibrationsQuery(int GageId) : IRequest<List<CalibrationRecordResponseModel>>;

public class GetGageCalibrationsHandler(AppDbContext db) : IRequestHandler<GetGageCalibrationsQuery, List<CalibrationRecordResponseModel>>
{
    public async Task<List<CalibrationRecordResponseModel>> Handle(GetGageCalibrationsQuery request, CancellationToken cancellationToken)
    {
        var exists = await db.Gages.AnyAsync(g => g.Id == request.GageId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Gage {request.GageId} not found");

        return await db.CalibrationRecords.AsNoTracking()
            .Where(c => c.GageId == request.GageId)
            .OrderByDescending(c => c.CalibratedAt)
            .Select(c => new CalibrationRecordResponseModel(
                c.Id, c.GageId, c.CalibratedById, c.CalibratedAt, c.Result,
                c.LabName, c.CertificateFileId, c.StandardsUsed,
                c.AsFoundCondition, c.AsLeftCondition, c.NextCalibrationDue,
                c.Notes))
            .ToListAsync(cancellationToken);
    }
}
