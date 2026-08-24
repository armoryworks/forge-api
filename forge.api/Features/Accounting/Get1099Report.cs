using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Annual 1099 report — every vendor flagged <see cref="Forge.Core.Entities.Vendor.Is1099"/>
/// with the total cash paid to it in the calendar <paramref name="Year"/>, for
/// preparing Form 1099-NEC filings. Sums <see cref="Forge.Core.Entities.VendorPayment.Amount"/>
/// (cash actually disbursed — the reportable figure), grouped in the query so
/// there is no per-vendor round-trip. Read-only; no <c>ActivityLog</c>.
///
/// <para>Gated on <c>CAP-ACCT-GL-VIEW</c> like the sibling sales-tax-liability and
/// aging reports on <see cref="Forge.Api.Controllers.AccountingGlController"/>.</para>
/// </summary>
[RequiresCapability("CAP-ACCT-GL-VIEW")]
public record Get1099ReportQuery(int Year) : IRequest<Form1099Report>;

public class Get1099ReportHandler(AppDbContext db)
    : IRequestHandler<Get1099ReportQuery, Form1099Report>
{
    /// <summary>IRS 1099-NEC reporting threshold — a vendor paid at least this in the year requires a filing.</summary>
    private const decimal ReportingThreshold = 600m;

    public async Task<Form1099Report> Handle(Get1099ReportQuery request, CancellationToken ct)
    {
        var vendors = await db.Vendors.AsNoTracking()
            .Where(v => v.Is1099)
            .Select(v => new { v.Id, v.CompanyName, v.VendorNumber, v.TaxId })
            .ToListAsync(ct);

        // Σ payments per vendor for the calendar year, grouped in the query — one
        // round-trip, no N+1. Restricted to the flagged vendors so the DB doesn't
        // aggregate the whole payment table.
        var flaggedIds = vendors.Select(v => v.Id).ToList();
        var totalsByVendor = flaggedIds.Count == 0
            ? []
            : await db.VendorPayments.AsNoTracking()
                .Where(p => flaggedIds.Contains(p.VendorId) && p.PaymentDate.Year == request.Year)
                .GroupBy(p => p.VendorId)
                .Select(g => new { VendorId = g.Key, Total = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.VendorId, x => x.Total, ct);

        var rows = vendors
            .Select(v =>
            {
                var total = totalsByVendor.TryGetValue(v.Id, out var t) ? t : 0m;
                return new Form1099VendorRow
                {
                    VendorId = v.Id,
                    VendorName = v.CompanyName,
                    VendorNumber = v.VendorNumber,
                    TaxId = v.TaxId,
                    TotalPayments = total,
                    MeetsThreshold = total >= ReportingThreshold,
                };
            })
            .OrderByDescending(r => r.TotalPayments)
            .ThenBy(r => r.VendorName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Form1099Report
        {
            Year = request.Year,
            Threshold = ReportingThreshold,
            Vendors = rows,
            TotalPayments = rows.Sum(r => r.TotalPayments),
            ReportableVendorCount = rows.Count(r => r.MeetsThreshold),
        };
    }
}
