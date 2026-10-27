using MediatR;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorCredits;

public record GenerateVendorCreditMemoPdfQuery(int Id, string? Language = null) : IRequest<byte[]>;

public class GenerateVendorCreditMemoPdfHandler(AppDbContext db, ISystemSettingRepository settings)
    : IRequestHandler<GenerateVendorCreditMemoPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateVendorCreditMemoPdfQuery request, CancellationToken ct)
    {
        var memo = await db.VendorCreditMemos.AsNoTracking()
            .Include(m => m.Vendor)
            .Include(m => m.VendorBill)
            .Include(m => m.VendorReturn)
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);
        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, await MoneyFormat.BaseCodeAsync(settings, ct), ct);
        return new VendorCreditMemoPdfDocument(memo, companyName, money, VendorDocumentPdfLabels.For(request.Language)).GeneratePdf();
    }
}
