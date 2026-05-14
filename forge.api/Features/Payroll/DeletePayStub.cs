using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public sealed record DeletePayStubCommand(int Id) : IRequest;

public sealed class DeletePayStubHandler(AppDbContext db)
    : IRequestHandler<DeletePayStubCommand>
{
    public async Task Handle(DeletePayStubCommand request, CancellationToken ct)
    {
        var stub = await db.PayStubs
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"PayStub {request.Id} not found");

        if (stub.Source != PayrollDocumentSource.Manual)
            throw new InvalidOperationException("Only manually uploaded pay stubs can be deleted.");

        stub.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
