using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetCompanyProfileQuery : IRequest<CompanyProfileResponseModel>;

public class GetCompanyProfileHandler(AppDbContext db)
    : IRequestHandler<GetCompanyProfileQuery, CompanyProfileResponseModel>
{
    public Task<CompanyProfileResponseModel> Handle(
        GetCompanyProfileQuery request, CancellationToken ct)
        => ReadAsync(db, ct);

    internal static async Task<CompanyProfileResponseModel> ReadAsync(AppDbContext db, CancellationToken ct)
    {
        var settings = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("company."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var companyZone = await BookTimeZoneRules.CompanyTimeZoneAsync(db, ct);
        var books = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.ReportingTimeZone })
            .ToListAsync(ct);

        var awaiting = 0;
        if (companyZone is not null)
        {
            foreach (var book in books.Where(b => b.ReportingTimeZone != companyZone))
            {
                if (!await BookTimeZoneRules.HasPostedEntriesAsync(db, book.Id, ct))
                    awaiting++;
            }
        }

        return new CompanyProfileResponseModel(
            Name: settings.GetValueOrDefault("company.name", ""),
            Phone: settings.GetValueOrDefault("company.phone", ""),
            Email: settings.GetValueOrDefault("company.email", ""),
            Ein: settings.GetValueOrDefault("company.ein", ""),
            Website: settings.GetValueOrDefault("company.website", ""),
            TimeZone: companyZone ?? books.Select(b => b.ReportingTimeZone).FirstOrDefault() ?? "",
            BooksAwaitingTimeZone: awaiting);
    }
}
