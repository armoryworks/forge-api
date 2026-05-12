using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record DeleteEdiTradingPartnerCommand(int Id) : IRequest;

public class DeleteEdiTradingPartnerHandler(AppDbContext db)
    : IRequestHandler<DeleteEdiTradingPartnerCommand>
{
    public async Task Handle(DeleteEdiTradingPartnerCommand request, CancellationToken cancellationToken)
    {
        var partner = await db.EdiTradingPartners
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Trading partner {request.Id} not found");

        partner.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
