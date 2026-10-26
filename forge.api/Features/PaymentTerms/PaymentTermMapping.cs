using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.PaymentTerms;

public static class PaymentTermMapping
{
    public static PaymentTermResponseModel ToResponse(PaymentTerm t)
        => new(t.Id, t.Code, t.Name, t.NetDays, t.DiscountPercent, t.DiscountDays,
            t.LegacyCreditTerms?.ToString(), t.IsActive, t.SortOrder, t.IsSystem);
}
