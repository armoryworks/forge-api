using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A conversion — dry run of the opening AR/AP open items before <c>POST opening-balances</c>: resolves
/// each row's customer or vendor (by id, or by number or name for a CSV row), checks the amount, the dates and
/// the document number the way the post will, and totals receivables and payables for the tie-out against
/// the legacy control balances. Writes nothing.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ValidateOpeningOpenItemsQuery(ValidateOpeningOpenItemsRequestModel Model)
    : IRequest<OpeningOpenItemsValidationModel>;

public class ValidateOpeningOpenItemsHandler(AppDbContext db)
    : IRequestHandler<ValidateOpeningOpenItemsQuery, OpeningOpenItemsValidationModel>
{
    public const string PartyNotFound = "party-not-found";
    public const string PartyAmbiguous = "party-ambiguous";
    public const string PartyTypeInvalid = "party-type-invalid";
    public const string AmountNotPositive = "amount-not-positive";
    public const string DueBeforeDocument = "due-before-document";
    public const string DocumentNumberDuplicate = "document-number-duplicate";
    public const string DocumentNumberInUse = "document-number-in-use";
    public const string DocumentNumberTooLong = "document-number-too-long";

    private const int InvoiceNumberMax = 20;
    private const int VendorInvoiceNumberMax = 60;

    private sealed record Party(int Id, string Name, string? Number, string? Alias);

    public async Task<OpeningOpenItemsValidationModel> Handle(
        ValidateOpeningOpenItemsQuery request, CancellationToken ct)
    {
        var model = request.Model;
        var rows = model.Rows ?? [];
        var customers = await LoadCustomersAsync(rows, ct);
        var vendors = await LoadVendorsAsync(rows, ct);

        var results = new List<OpeningOpenItemRowResultModel>(rows.Count);
        var resolved = new List<(OpeningOpenItemRowModel Row, int? PartyId, List<string> Errors)>(rows.Count);
        foreach (var row in rows)
        {
            var errors = new List<string>();
            var parties = row.PartyType switch
            {
                SubledgerPartyType.Customer => customers,
                SubledgerPartyType.Vendor => vendors,
                _ => null,
            };
            Party? party = null;
            if (parties is null)
                errors.Add(PartyTypeInvalid);
            else
            {
                var matches = Match(parties, row);
                if (matches.Count == 0) errors.Add(PartyNotFound);
                else if (matches.Count > 1) errors.Add(PartyAmbiguous);
                else party = matches[0];
            }

            if (row.Amount <= 0m) errors.Add(AmountNotPositive);
            if (row.DueDate is DateOnly due && row.DocumentDate is DateOnly issued && due < issued)
                errors.Add(DueBeforeDocument);

            var number = row.DocumentNumber?.Trim();
            if (!string.IsNullOrEmpty(number)
                && number.Length > (row.PartyType == SubledgerPartyType.Vendor ? VendorInvoiceNumberMax : InvoiceNumberMax))
                errors.Add(DocumentNumberTooLong);

            resolved.Add((row, party?.Id, errors));
        }

        await CheckDocumentNumbersAsync(resolved, ct);

        for (var i = 0; i < resolved.Count; i++)
        {
            var (row, partyId, errors) = resolved[i];
            var parties = row.PartyType == SubledgerPartyType.Vendor ? vendors : customers;
            var name = partyId is int id ? parties.FirstOrDefault(p => p.Id == id)?.Name : null;
            results.Add(new OpeningOpenItemRowResultModel(i + 1, partyId, name, errors));
        }

        var key = $"{JournalSource.Conversion}:OpeningBalance:{model.BookId}";
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(e => e.BookId == model.BookId && e.IdempotencyKey == key, ct);

        return new OpeningOpenItemsValidationModel(
            alreadyPosted,
            rows.Where(r => r.PartyType == SubledgerPartyType.Customer).Sum(r => r.Amount),
            rows.Where(r => r.PartyType == SubledgerPartyType.Vendor).Sum(r => r.Amount),
            results.Sum(r => r.Errors.Count),
            results);
    }

    private static List<Party> Match(List<Party> parties, OpeningOpenItemRowModel row)
    {
        if (row.PartyId is int id)
            return parties.Where(p => p.Id == id).ToList();
        var reference = row.PartyRef?.Trim();
        if (string.IsNullOrEmpty(reference))
            return [];
        var byNumber = parties.Where(p => string.Equals(p.Number, reference, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byNumber.Count > 0)
            return byNumber;
        return parties
            .Where(p => string.Equals(p.Name, reference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Alias, reference, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static (List<int> Ids, List<string> Refs) Keys(IEnumerable<OpeningOpenItemRowModel> rows)
    {
        var list = rows.ToList();
        return (
            list.Where(r => r.PartyId is not null).Select(r => r.PartyId!.Value).Distinct().ToList(),
            list.Where(r => r.PartyId is null && !string.IsNullOrWhiteSpace(r.PartyRef))
                .Select(r => r.PartyRef!.Trim().ToLower()).Distinct().ToList());
    }

    private async Task<List<Party>> LoadCustomersAsync(List<OpeningOpenItemRowModel> rows, CancellationToken ct)
    {
        var (ids, refs) = Keys(rows.Where(r => r.PartyType == SubledgerPartyType.Customer));
        if (ids.Count == 0 && refs.Count == 0)
            return [];
        return await db.Customers.AsNoTracking()
            .Where(c => ids.Contains(c.Id)
                || (c.CustomerNumber != null && refs.Contains(c.CustomerNumber.ToLower()))
                || refs.Contains(c.Name.ToLower())
                || (c.CompanyName != null && refs.Contains(c.CompanyName.ToLower())))
            .Select(c => new Party(c.Id, c.CompanyName ?? c.Name, c.CustomerNumber, c.Name))
            .ToListAsync(ct);
    }

    private async Task<List<Party>> LoadVendorsAsync(List<OpeningOpenItemRowModel> rows, CancellationToken ct)
    {
        var (ids, refs) = Keys(rows.Where(r => r.PartyType == SubledgerPartyType.Vendor));
        if (ids.Count == 0 && refs.Count == 0)
            return [];
        return await db.Vendors.AsNoTracking()
            .Where(v => ids.Contains(v.Id)
                || (v.VendorNumber != null && refs.Contains(v.VendorNumber.ToLower()))
                || refs.Contains(v.CompanyName.ToLower()))
            .Select(v => new Party(v.Id, v.CompanyName, v.VendorNumber, null))
            .ToListAsync(ct);
    }

    private async Task CheckDocumentNumbersAsync(
        List<(OpeningOpenItemRowModel Row, int? PartyId, List<string> Errors)> rows, CancellationToken ct)
    {
        var numbered = rows
            .Select(r => (r.Row, r.PartyId, r.Errors, Number: r.Row.DocumentNumber?.Trim()))
            .Where(r => !string.IsNullOrEmpty(r.Number))
            .ToList();

        foreach (var group in numbered
                     .GroupBy(r => (r.Row.PartyType, Vendor: r.Row.PartyType == SubledgerPartyType.Vendor ? r.PartyId : null, r.Number))
                     .Where(g => g.Count() > 1))
            foreach (var row in group)
                row.Errors.Add(DocumentNumberDuplicate);

        var invoiceNumbers = numbered
            .Where(r => r.Row.PartyType == SubledgerPartyType.Customer)
            .Select(r => r.Number!)
            .Distinct()
            .ToList();
        HashSet<string> takenInvoices = invoiceNumbers.Count == 0
            ? []
            : (await db.Invoices.IgnoreQueryFilters().AsNoTracking()
                .Where(i => invoiceNumbers.Contains(i.InvoiceNumber))
                .Select(i => i.InvoiceNumber)
                .ToListAsync(ct)).ToHashSet();

        var vendorIds = numbered
            .Where(r => r.Row.PartyType == SubledgerPartyType.Vendor && r.PartyId is not null)
            .Select(r => r.PartyId!.Value)
            .Distinct()
            .ToList();
        HashSet<(int, string)> takenBills = vendorIds.Count == 0
            ? []
            : (await db.VendorBills.IgnoreQueryFilters().AsNoTracking()
                .Where(b => vendorIds.Contains(b.VendorId) && b.VendorInvoiceNumber != null)
                .Select(b => new { b.VendorId, b.VendorInvoiceNumber })
                .ToListAsync(ct))
                .Select(b => (b.VendorId, b.VendorInvoiceNumber!))
                .ToHashSet();

        foreach (var row in numbered)
        {
            var taken = row.Row.PartyType == SubledgerPartyType.Customer
                ? takenInvoices.Contains(row.Number!)
                : row.PartyId is int vendorId && takenBills.Contains((vendorId, row.Number!));
            if (taken)
                row.Errors.Add(DocumentNumberInUse);
        }
    }
}
