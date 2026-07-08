using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Terms;

public record GetTermsDocumentsQuery(
    TermsScope? Scope,
    int? CustomerId,
    int? PartId,
    bool? IsActive) : IRequest<List<TermsDocumentResponseModel>>;

public class GetTermsDocumentsHandler(AppDbContext db)
    : IRequestHandler<GetTermsDocumentsQuery, List<TermsDocumentResponseModel>>
{
    public async Task<List<TermsDocumentResponseModel>> Handle(
        GetTermsDocumentsQuery request, CancellationToken ct)
    {
        var query = db.TermsDocuments
            .AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.Part)
            .AsQueryable();

        if (request.Scope.HasValue)
            query = query.Where(d => d.Scope == request.Scope.Value);
        if (request.CustomerId.HasValue)
            query = query.Where(d => d.CustomerId == request.CustomerId.Value);
        if (request.PartId.HasValue)
            query = query.Where(d => d.PartId == request.PartId.Value);
        if (request.IsActive.HasValue)
            query = query.Where(d => d.IsActive == request.IsActive.Value);

        var docs = await query
            .OrderBy(d => d.Scope)
            .ThenBy(d => d.SortOrder)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        return docs.Select(TermsDocumentMapper.ToResponseModel).ToList();
    }
}
