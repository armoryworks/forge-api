using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>A book's maker-checker threshold and reporting time zone, with whether the zone is locked.</summary>
[RequiresCapability("CAP-ACCT-GL-VIEW")]
public record GetBookSettingsQuery(int BookId) : IRequest<BookSettingsModel>;

public class GetBookSettingsHandler(AppDbContext db) : IRequestHandler<GetBookSettingsQuery, BookSettingsModel>
{
    public async Task<BookSettingsModel> Handle(GetBookSettingsQuery request, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId, ct)
            ?? throw new KeyNotFoundException($"Book {request.BookId} not found.");

        return await BuildAsync(db, book, ct);
    }

    internal static async Task<BookSettingsModel> BuildAsync(AppDbContext db, Book book, CancellationToken ct)
        => new(
            book.Id,
            book.Code,
            book.Name,
            book.MakerCheckerThreshold,
            book.ReportingTimeZone,
            await BookTimeZoneRules.HasPostedEntriesAsync(db, book.Id, ct),
            await BookTimeZoneRules.CompanyTimeZoneAsync(db, ct));
}
