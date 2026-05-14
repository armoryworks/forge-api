using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public sealed record DeleteTaxDocumentCommand(int Id) : IRequest;

public sealed class DeleteTaxDocumentHandler(AppDbContext db)
    : IRequestHandler<DeleteTaxDocumentCommand>
{
    public async Task Handle(DeleteTaxDocumentCommand request, CancellationToken ct)
    {
        var doc = await db.TaxDocuments
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"TaxDocument {request.Id} not found");

        if (doc.Source != PayrollDocumentSource.Manual)
            throw new InvalidOperationException("Only manually uploaded tax documents can be deleted.");

        doc.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
