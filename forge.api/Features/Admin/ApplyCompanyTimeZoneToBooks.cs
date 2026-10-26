using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Admin;

/// <summary>
/// Copies the company time zone onto every active book that has no posted entry yet (a book with posted
/// entries keeps its zone: its fiscal calendar is fixed). Returns how many books changed and how many were
/// left alone because they are locked.
/// </summary>
public record ApplyCompanyTimeZoneToBooksCommand : IRequest<ApplyCompanyTimeZoneResultModel>;

public class ApplyCompanyTimeZoneToBooksHandler(AppDbContext db)
    : IRequestHandler<ApplyCompanyTimeZoneToBooksCommand, ApplyCompanyTimeZoneResultModel>
{
    public async Task<ApplyCompanyTimeZoneResultModel> Handle(
        ApplyCompanyTimeZoneToBooksCommand request, CancellationToken ct)
    {
        var zone = await BookTimeZoneRules.CompanyTimeZoneAsync(db, ct)
            ?? throw new InvalidOperationException("Set the company time zone before applying it to the books.");

        var books = await db.Books.Where(b => b.IsActive && b.ReportingTimeZone != zone).ToListAsync(ct);
        var updated = 0;
        var locked = 0;
        foreach (var book in books)
        {
            if (await BookTimeZoneRules.HasPostedEntriesAsync(db, book.Id, ct))
            {
                locked++;
                continue;
            }

            db.LogActivityAt(
                "book-settings-updated",
                $"Book {book.Code} settings: reporting time zone {book.ReportingTimeZone} → {zone} (company time zone)",
                ("Book", book.Id));
            book.ReportingTimeZone = zone;
            updated++;
        }

        if (updated > 0)
            await db.SaveChangesAsync(ct);

        return new ApplyCompanyTimeZoneResultModel(zone, updated, locked);
    }
}
