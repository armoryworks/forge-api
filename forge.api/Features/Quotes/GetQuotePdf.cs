using MediatR;
using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Quotes;

public record GetQuotePdfQuery(int Id) : IRequest<byte[]>;

public class GetQuotePdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    ITermsCompilationService compiler) : IRequestHandler<GetQuotePdfQuery, byte[]>
{
    public const string CompanyNameSettingKey = "company.name";

    public async Task<byte[]> Handle(GetQuotePdfQuery request, CancellationToken ct)
    {
        var quote = await db.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(q => q.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        var companyName = await ReadCompanyNameAsync(settings, ct);

        var partIds = quote.Lines
            .Where(l => l.PartId.HasValue)
            .Select(l => l.PartId!.Value)
            .Distinct()
            .ToList();
        var compiled = await compiler.CompileForQuoteAsync(quote.CustomerId, partIds, ct);

        return new QuotePdfDocument(quote, companyName, compiled.Sections).GeneratePdf();
    }

    public static async Task<string?> ReadCompanyNameAsync(ISystemSettingRepository settings, CancellationToken ct)
    {
        var companyName = (await settings.FindByKeyAsync(CompanyNameSettingKey, ct))?.Value?.Trim();
        return string.IsNullOrEmpty(companyName) ? null : companyName;
    }
}
