using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record CloseCustomerReturnCommand(int Id) : IRequest;

public class CloseCustomerReturnHandler(AppDbContext db)
    : IRequestHandler<CloseCustomerReturnCommand>
{
    public async Task Handle(CloseCustomerReturnCommand request, CancellationToken ct)
    {
        var ret = await db.CustomerReturns.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        if (ret.Status != CustomerReturnStatus.Resolved)
            throw new InvalidOperationException("Only resolved returns can be closed");

        ret.Status = CustomerReturnStatus.Closed;
        await db.SaveChangesAsync(ct);
    }
}
