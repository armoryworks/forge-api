using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>
/// What a create or update asked for: a terms id (the picker), legacy credit terms text (older callers), a
/// clear, or nothing. <see cref="ResolveAsync"/> turns it into the terms row plus the legacy value to keep in
/// step, or reports that nothing was asked for.
/// </summary>
public sealed record TermsChoice(int? PaymentTermsId, string? LegacyText, bool Clear)
{
    public bool IsEmpty => !Clear && PaymentTermsId is null && string.IsNullOrWhiteSpace(LegacyText);

    /// <summary>(asked, terms, legacy). Terms the party already has may be inactive; a new pick may not.</summary>
    public async Task<(bool Asked, PaymentTerm? Terms, CreditTerms? Legacy)> ResolveAsync(
        PaymentTermsResolver resolver, int? currentTermsId, string field, CancellationToken ct)
    {
        if (Clear)
            return (true, null, null);

        if (PaymentTermsId is int id)
        {
            var terms = id == currentTermsId
                ? await resolver.FindAsync(id, ct) ?? await resolver.RequireAsync(id, field, ct)
                : await resolver.RequireAsync(id, field, ct);
            return (true, terms, terms.LegacyCreditTerms);
        }

        if (CreditTermsExtensions.TryParseLenient(LegacyText, out var legacy))
            return (true, await resolver.ForLegacyAsync(legacy, ct), legacy);

        return (false, null, null);
    }
}
