using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetMrpExceptionsQuery(int? MrpRunId, bool? UnresolvedOnly) : IRequest<List<MrpExceptionResponseModel>>;

public class GetMrpExceptionsHandler(AppDbContext db)
    : IRequestHandler<GetMrpExceptionsQuery, List<MrpExceptionResponseModel>>
{
    public async Task<List<MrpExceptionResponseModel>> Handle(GetMrpExceptionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.MrpExceptions
            .AsNoTracking()
            .Include(e => e.Part)
            .AsQueryable();

        if (request.MrpRunId.HasValue)
            query = query.Where(e => e.MrpRunId == request.MrpRunId.Value);

        if (request.UnresolvedOnly == true)
            query = query.Where(e => !e.IsResolved);

        return await query
            .OrderByDescending(e => e.MrpRunId)
            .ThenBy(e => e.ExceptionType)
            .Select(e => new MrpExceptionResponseModel(
                e.Id,
                e.MrpRunId,
                e.PartId,
                e.Part.PartNumber,
                e.Part.Description ?? e.Part.Name,
                e.ExceptionType,
                e.Message,
                e.SuggestedAction,
                e.IsResolved,
                e.ResolvedByUserId,
                e.ResolvedAt,
                e.ResolutionNotes
            ))
            .ToListAsync(cancellationToken);
    }
}
