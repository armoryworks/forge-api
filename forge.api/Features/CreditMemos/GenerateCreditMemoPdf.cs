using MediatR;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.CreditMemos;

public record GenerateCreditMemoPdfQuery(int Id) : IRequest<byte[]>;

public class GenerateCreditMemoPdfHandler(AppDbContext db, ISystemSettingRepository settings)
    : IRequestHandler<GenerateCreditMemoPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateCreditMemoPdfQuery request, CancellationToken ct)
    {
        var memo = await db.CreditMemos.AsNoTracking()
            .Include(m => m.Customer)
            .Include(m => m.Invoice)
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .Include(m => m.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);
        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, await MoneyFormat.BaseCodeAsync(settings, ct), ct);
        return new CreditMemoPdfDocument(memo, companyName, money).GeneratePdf();
    }
}
