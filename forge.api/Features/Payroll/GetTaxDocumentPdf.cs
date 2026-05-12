using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record GetTaxDocumentPdfQuery(int Id, int RequestingUserId, bool IsAdmin) : IRequest<int?>;

public class GetTaxDocumentPdfHandler(AppDbContext db)
    : IRequestHandler<GetTaxDocumentPdfQuery, int?>
{
    public async Task<int?> Handle(GetTaxDocumentPdfQuery request, CancellationToken ct)
    {
        var doc = await db.TaxDocuments
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => new { d.UserId, d.FileAttachmentId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"TaxDocument {request.Id} not found");

        if (!request.IsAdmin && doc.UserId != request.RequestingUserId)
            throw new UnauthorizedAccessException("You do not have access to this tax document.");

        return doc.FileAttachmentId;
    }
}
