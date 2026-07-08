using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Terms;

/// <summary>
/// S3 — compiled terms sections for a quote (company + the quote's customer +
/// its line parts), as of now. Backs the send-quote dialog's preview pane;
/// read-only, nothing is persisted (the immutable snapshot happens on send).
/// </summary>
public record PreviewQuoteTermsQuery(int QuoteId) : IRequest<CompiledTermsResult>;

public class PreviewQuoteTermsHandler(AppDbContext db, ITermsCompilationService compiler)
    : IRequestHandler<PreviewQuoteTermsQuery, CompiledTermsResult>
{
    public async Task<CompiledTermsResult> Handle(PreviewQuoteTermsQuery request, CancellationToken ct)
    {
        var quote = await db.Quotes
            .AsNoTracking()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.QuoteId, ct)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found");

        var partIds = quote.Lines
            .Where(l => l.PartId.HasValue)
            .Select(l => l.PartId!.Value)
            .Distinct()
            .ToList();

        return await compiler.CompileForQuoteAsync(quote.CustomerId, partIds, ct);
    }
}
