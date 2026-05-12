using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record GetPayStubPdfQuery(int Id, int RequestingUserId, bool IsAdmin) : IRequest<int?>;

public class GetPayStubPdfHandler(AppDbContext db)
    : IRequestHandler<GetPayStubPdfQuery, int?>
{
    public async Task<int?> Handle(GetPayStubPdfQuery request, CancellationToken ct)
    {
        var stub = await db.PayStubs
            .AsNoTracking()
            .Where(p => p.Id == request.Id)
            .Select(p => new { p.UserId, p.FileAttachmentId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"PayStub {request.Id} not found");

        if (!request.IsAdmin && stub.UserId != request.RequestingUserId)
            throw new UnauthorizedAccessException("You do not have access to this pay stub.");

        return stub.FileAttachmentId;
    }
}
