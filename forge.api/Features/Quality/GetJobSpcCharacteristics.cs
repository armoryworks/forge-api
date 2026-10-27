using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetJobSpcCharacteristicsQuery(int JobId) : IRequest<List<SpcCharacteristicResponseModel>>;

public class GetJobSpcCharacteristicsHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<GetJobSpcCharacteristicsQuery, List<SpcCharacteristicResponseModel>>
{
    public async Task<List<SpcCharacteristicResponseModel>> Handle(
        GetJobSpcCharacteristicsQuery request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.PartId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.PartId is not int partId)
            return [];

        return await mediator.Send(new GetSpcCharacteristicsQuery(partId, null, true), cancellationToken);
    }
}
