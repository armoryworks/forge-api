using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>
/// Keeps a vendor's terms id and its free-text terms in step. A picked terms id writes the terms' name into the
/// text; typed text is matched to terms where it can be ("Net 30", "2/10 net 30") and kept as typed either way.
/// </summary>
public static class VendorTerms
{
    /// <summary>Applies the choice; true when the vendor changed.</summary>
    public static async Task<bool> ApplyAsync(
        AppDbContext db, Vendor vendor, int? paymentTermsId, string? text, CancellationToken ct)
    {
        var resolver = new PaymentTermsResolver(db);

        if (paymentTermsId is int id)
        {
            var terms = id == vendor.PaymentTermsId
                ? await resolver.FindAsync(id, ct) ?? await resolver.RequireAsync(id, "PaymentTermsId", ct)
                : await resolver.RequireAsync(id, "PaymentTermsId", ct);
            if (vendor.PaymentTermsId == terms.Id && vendor.PaymentTerms == terms.Name)
                return false;
            vendor.PaymentTermsId = terms.Id;
            vendor.PaymentTerms = terms.Name;
            return true;
        }

        if (text is null || text == vendor.PaymentTerms)
            return false;

        var trimmed = text.Trim();
        vendor.PaymentTerms = trimmed.Length == 0 ? null : text;
        vendor.PaymentTermsId = (await resolver.MatchTextAsync(trimmed, ct))?.Id;
        return true;
    }
}
