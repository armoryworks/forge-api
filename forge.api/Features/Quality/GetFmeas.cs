using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetFmeasQuery(FmeaType? Type, int? PartId, FmeaStatus? Status) : IRequest<List<FmeaResponseModel>>;

public class GetFmeasHandler(AppDbContext db)
    : IRequestHandler<GetFmeasQuery, List<FmeaResponseModel>>
{
    private const int HighRpnThreshold = 200;

    public async Task<List<FmeaResponseModel>> Handle(
        GetFmeasQuery request, CancellationToken cancellationToken)
    {
        var query = db.FmeaAnalyses
            .AsNoTracking()
            .Include(f => f.Part)
            .Include(f => f.Operation)
            .Include(f => f.Items)
            .AsQueryable();

        if (request.Type.HasValue)
            query = query.Where(f => f.Type == request.Type.Value);
        if (request.PartId.HasValue)
            query = query.Where(f => f.PartId == request.PartId.Value);
        if (request.Status.HasValue)
            query = query.Where(f => f.Status == request.Status.Value);

        var fmeas = await query
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

        return fmeas.Select(f =>
        {
            var rpns = f.Items.Select(i => i.Severity * i.Occurrence * i.Detection).ToList();
            return new FmeaResponseModel
            {
                Id = f.Id,
                FmeaNumber = f.FmeaNumber,
                Name = f.Name,
                Type = f.Type,
                PartId = f.PartId,
                PartNumber = f.Part?.PartNumber,
                OperationId = f.OperationId,
                OperationName = f.Operation?.Title,
                Status = f.Status,
                PreparedBy = f.PreparedBy,
                Responsibility = f.Responsibility,
                OriginalDate = f.OriginalDate,
                RevisionDate = f.RevisionDate,
                RevisionNumber = f.RevisionNumber,
                PpapSubmissionId = f.PpapSubmissionId,
                HighRpnCount = rpns.Count(r => r > HighRpnThreshold),
                MaxRpn = rpns.Count > 0 ? rpns.Max() : 0,
                Items = [],
                CreatedAt = f.CreatedAt,
            };
        }).ToList();
    }
}
