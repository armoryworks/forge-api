using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers;

/// <summary>
/// Flat cross-customer contact listing — powers /customers/contacts.
/// Includes per-row outreach-preferences summary so the "who has
/// email opt-out across every customer?" question is answerable in
/// one query.
/// </summary>
public record GetAllContactsFlatQuery() : IRequest<List<FlatContactRowModel>>;

public class GetAllContactsFlatHandler(AppDbContext db) : IRequestHandler<GetAllContactsFlatQuery, List<FlatContactRowModel>>
{
    public async Task<List<FlatContactRowModel>> Handle(GetAllContactsFlatQuery request, CancellationToken ct)
    {
        return await (
            from c in db.Contacts.AsNoTracking()
            join cust in db.Customers.AsNoTracking() on c.CustomerId equals cust.Id
            from p in db.ContactOutreachPreferences.AsNoTracking().Where(p => p.ContactId == c.Id).DefaultIfEmpty()
            orderby c.IsPrimary descending, c.LastName
            select new FlatContactRowModel(
                c.Id, c.CustomerId, cust.Name, cust.CompanyName,
                c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary,
                p != null && p.EmailOptOut,
                p != null && p.CallOptOut,
                p != null && p.CooldownUntil != null && p.CooldownUntil > DateTimeOffset.UtcNow)
        ).ToListAsync(ct);
    }
}
