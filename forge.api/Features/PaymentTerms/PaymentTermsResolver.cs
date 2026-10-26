using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>
/// Finds the payment terms a party or document carries. A row with no terms id falls back to the terms
/// that stand for its legacy credit terms, so documents written before payment terms existed (or by callers
/// that still send only credit terms) read the same as new ones.
/// </summary>
public sealed class PaymentTermsResolver(AppDbContext db)
{
    public const string NotFoundCode = "payment-terms-not-found";
    public const string InactiveCode = "payment-terms-inactive";
    public const string DueDateCode = "payment-terms-due-date";

    public Task<PaymentTerm?> FindAsync(int? id, CancellationToken ct)
        => id is int termsId
            ? db.PaymentTerms.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == termsId, ct)
            : Task.FromResult<PaymentTerm?>(null);

    /// <summary>The terms a caller picked by id: refused (400) when they do not exist or are inactive.</summary>
    public async Task<PaymentTerm> RequireAsync(int id, string field, CancellationToken ct)
    {
        var terms = await db.PaymentTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw Failure(field, NotFoundCode, $"Payment terms {id} were not found.");
        if (!terms.IsActive)
            throw Failure(field, InactiveCode, $"Payment terms {terms.Name} are inactive.");
        return terms;
    }

    public Task<PaymentTerm?> ForLegacyAsync(CreditTerms? legacy, CancellationToken ct)
        => legacy is CreditTerms value
            ? db.PaymentTerms.AsNoTracking().FirstOrDefaultAsync(t => t.LegacyCreditTerms == value, ct)
            : Task.FromResult<PaymentTerm?>(null);

    /// <summary>The terms id when set, else the terms for the legacy credit terms.</summary>
    public async Task<PaymentTerm?> EffectiveAsync(int? id, CreditTerms? legacy, CancellationToken ct)
        => id is int ? await FindAsync(id, ct) : await ForLegacyAsync(legacy, ct);

    /// <summary>Active terms whose code or name matches free text ("Net 30", "2/10 net 30"), or null.</summary>
    public async Task<PaymentTerm?> MatchTextAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var all = await db.PaymentTerms.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);
        return all.FirstOrDefault(t => PaymentTermsText.Matches(text, t.Code, t.Name));
    }

    /// <summary>Read models for a set of terms ids (unknown ids are left out).</summary>
    public async Task<IReadOnlyDictionary<int, PaymentTermsModel>> ModelsAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var wanted = ids.OfType<int>().Distinct().ToList();
        if (wanted.Count == 0)
            return new Dictionary<int, PaymentTermsModel>();
        var rows = await db.PaymentTerms.AsNoTracking().IgnoreQueryFilters()
            .Where(t => wanted.Contains(t.Id))
            .ToListAsync(ct);
        return rows.ToDictionary(t => t.Id, t => ToModel(t)!);
    }

    public async Task<PaymentTermsModel?> ModelAsync(int? id, CreditTerms? legacy, CancellationToken ct)
        => ToModel(await EffectiveAsync(id, legacy, ct));

    public static PaymentTermsModel? ToModel(PaymentTerm? terms)
        => terms is null
            ? null
            : new PaymentTermsModel(terms.Id, terms.Code, terms.Name, terms.NetDays, terms.DiscountPercent, terms.DiscountDays);

    /// <summary>Copies the terms onto an invoice: id, legacy credit terms and the discount snapshot.</summary>
    public static void Stamp(Invoice invoice, PaymentTerm? terms)
    {
        invoice.PaymentTermsId = terms?.Id;
        invoice.CreditTerms = terms?.LegacyCreditTerms;
        invoice.DiscountPercent = terms?.HasDiscount == true ? terms.DiscountPercent : 0m;
        invoice.DiscountDueDate = terms?.HasDiscount == true
            ? EarlyPaymentDiscount.DiscountDueDate(invoice.InvoiceDate, terms.DiscountPercent, terms.DiscountDays)
            : null;
    }

    /// <summary>Copies the terms onto a bill: id, legacy credit terms and the discount snapshot.</summary>
    public static void Stamp(VendorBill bill, PaymentTerm? terms)
    {
        bill.PaymentTermsId = terms?.Id;
        bill.CreditTerms = terms?.LegacyCreditTerms;
        bill.DiscountPercent = terms?.HasDiscount == true ? terms.DiscountPercent : 0m;
        bill.DiscountDueDate = terms?.HasDiscount == true
            ? EarlyPaymentDiscount.DiscountDueDate(bill.BillDate, terms.DiscountPercent, terms.DiscountDays)
            : null;
    }

    public static ValidationException Failure(string field, string code, string message)
        => new([new ValidationFailure(field, message) { ErrorCode = code }]);
}
