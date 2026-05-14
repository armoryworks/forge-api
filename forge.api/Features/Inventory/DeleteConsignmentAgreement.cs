using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record DeleteConsignmentAgreementCommand(int Id) : IRequest;

public class DeleteConsignmentAgreementHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteConsignmentAgreementCommand>
{
    public async Task Handle(DeleteConsignmentAgreementCommand request, CancellationToken cancellationToken)
    {
        var agreement = await db.ConsignmentAgreements
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Consignment agreement {request.Id} not found");

        agreement.Status = ConsignmentAgreementStatus.Terminated;
        agreement.DeletedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
