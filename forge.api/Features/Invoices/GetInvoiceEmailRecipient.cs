using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GetInvoiceEmailRecipientQuery(int Id) : IRequest<InvoiceEmailRecipientResponseModel>;

public class GetInvoiceEmailRecipientHandler(AppDbContext db)
    : IRequestHandler<GetInvoiceEmailRecipientQuery, InvoiceEmailRecipientResponseModel>
{
    public async Task<InvoiceEmailRecipientResponseModel> Handle(
        GetInvoiceEmailRecipientQuery request, CancellationToken cancellationToken)
    {
        var customerId = await db.Invoices.AsNoTracking()
            .Where(i => i.Id == request.Id)
            .Select(i => (int?)i.CustomerId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        return await InvoiceRecipientResolver.ResolveAsync(db, customerId, cancellationToken);
    }
}
