using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>One journal template with its lines. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetJournalTemplateQuery(int TemplateId) : IRequest<JournalTemplateModel>;

public class GetJournalTemplateHandler(IJournalTemplateService service)
    : IRequestHandler<GetJournalTemplateQuery, JournalTemplateModel>
{
    public async Task<JournalTemplateModel> Handle(GetJournalTemplateQuery request, CancellationToken ct)
        => await service.GetAsync(request.TemplateId, ct)
           ?? throw new KeyNotFoundException($"Journal template {request.TemplateId} not found.");
}

/// <summary>
/// Customers or vendors matching a search, for naming the counterparty on a control-account template line.
/// Active parties only, capped at 25. CAP-ACCT-FULLGL gated.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record SearchJournalPartiesQuery(SubledgerPartyType PartyType, string? Search, int? Id = null)
    : IRequest<IReadOnlyList<JournalPartyOptionModel>>;

public class SearchJournalPartiesHandler(AppDbContext db)
    : IRequestHandler<SearchJournalPartiesQuery, IReadOnlyList<JournalPartyOptionModel>>
{
    private const int Limit = 25;

    public async Task<IReadOnlyList<JournalPartyOptionModel>> Handle(SearchJournalPartiesQuery request, CancellationToken ct)
    {
        var term = request.Search?.Trim().ToLowerInvariant() ?? string.Empty;

        if (request.PartyType == SubledgerPartyType.Customer)
        {
            var customers = db.Customers.AsNoTracking();
            customers = request.Id is int customerId
                ? customers.Where(c => c.Id == customerId)
                : customers.Where(c => c.IsActive && (term == string.Empty
                    || c.Name.ToLower().Contains(term) || (c.CompanyName != null && c.CompanyName.ToLower().Contains(term))));
            return await customers
                .OrderBy(c => c.Name)
                .Take(Limit)
                .Select(c => new JournalPartyOptionModel(SubledgerPartyType.Customer, c.Id, c.Name))
                .ToListAsync(ct);
        }

        var vendors = db.Vendors.AsNoTracking();
        vendors = request.Id is int vendorId
            ? vendors.Where(v => v.Id == vendorId)
            : vendors.Where(v => v.IsActive && (term == string.Empty || v.CompanyName.ToLower().Contains(term)));
        return await vendors
            .OrderBy(v => v.CompanyName)
            .Take(Limit)
            .Select(v => new JournalPartyOptionModel(SubledgerPartyType.Vendor, v.Id, v.CompanyName))
            .ToListAsync(ct);
    }
}
