using MediatR;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record ResolveCustomerReturnCommand(int Id) : IRequest;

public class ResolveCustomerReturnHandler(AppDbContext db)
    : IRequestHandler<ResolveCustomerReturnCommand>
{
    public async Task Handle(ResolveCustomerReturnCommand request, CancellationToken ct)
    {
        var ret = await db.CustomerReturns.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        if (ret.Status == CustomerReturnStatus.Closed)
            throw new InvalidOperationException("Cannot resolve a closed return");

        ret.Status = CustomerReturnStatus.Resolved;
        await db.SaveChangesAsync(ct);
    }
}
