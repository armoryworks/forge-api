using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A conversion read: the book the opening journal targets (the requested one, or the first active
/// book) with its functional currency, its posted Conversion entry if any, and whether CAP-ACCT-FULLGL
/// is on. Reachable under <c>CAP-ACCT-MIGRATION</c> while FULLGL is still off.
/// </summary>
[RequiresCapability("CAP-ACCT-MIGRATION")]
public record GetConversionStatusQuery(int? BookId = null) : IRequest<ConversionStatusModel>;

public class GetConversionStatusHandler(AppDbContext db, ICapabilitySnapshotProvider capabilities)
    : IRequestHandler<GetConversionStatusQuery, ConversionStatusModel>
{
    public async Task<ConversionStatusModel> Handle(GetConversionStatusQuery request, CancellationToken ct)
    {
        var books = db.Books.AsNoTracking().Where(b => b.IsActive);
        if (request.BookId is int bookId)
            books = books.Where(b => b.Id == bookId);
        var book = await books
            .OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.Name, b.FunctionalCurrencyId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException(request.BookId is int id
                ? $"Book {id} not found or inactive."
                : "No active accounting book exists.");

        var opening = await db.JournalEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.BookId == book.Id
                && e.Source == JournalSource.Conversion
                && e.Status == JournalEntryStatus.Posted)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.EntryNumber, e.EntryDate })
            .FirstOrDefaultAsync(ct);

        return new ConversionStatusModel(
            book.Id,
            book.Name,
            book.FunctionalCurrencyId,
            opening is not null,
            opening?.Id,
            opening?.EntryNumber,
            opening?.EntryDate,
            capabilities.IsEnabled("CAP-ACCT-FULLGL"));
    }
}
