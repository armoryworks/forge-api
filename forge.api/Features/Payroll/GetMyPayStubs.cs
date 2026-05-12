using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record GetMyPayStubsQuery(int UserId) : IRequest<List<PayStubResponseModel>>;

public class GetMyPayStubsHandler(AppDbContext db)
    : IRequestHandler<GetMyPayStubsQuery, List<PayStubResponseModel>>
{
    public async Task<List<PayStubResponseModel>> Handle(
        GetMyPayStubsQuery request, CancellationToken ct)
    {
        var stubs = await db.PayStubs
            .AsNoTracking()
            .Include(p => p.Deductions)
            .Where(p => p.UserId == request.UserId)
            .OrderByDescending(p => p.PayDate)
            .ToListAsync(ct);

        return stubs.Select(p => new PayStubResponseModel(
            p.Id, p.UserId, p.PayPeriodStart, p.PayPeriodEnd, p.PayDate,
            p.GrossPay, p.NetPay, p.TotalDeductions, p.TotalTaxes,
            p.FileAttachmentId, p.Source, p.ExternalId,
            p.Deductions.Select(d => new PayStubDeductionResponseModel(
                d.Id, d.Category, d.Description, d.Amount)).ToList()
        )).ToList();
    }
}
