using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Customers;

/// <summary>
/// Phase 1r — admin oversight of customer-portal access. Lists every
/// CustomerPortalAccess row across all customers with last-login
/// timestamp + IsEnabled flag. Powers /customers/portal-access.
/// </summary>
public record ListPortalAccessQuery() : IRequest<List<PortalAccessRowModel>>;

public class ListPortalAccessHandler(AppDbContext db) : IRequestHandler<ListPortalAccessQuery, List<PortalAccessRowModel>>
{
    public async Task<List<PortalAccessRowModel>> Handle(ListPortalAccessQuery request, CancellationToken ct)
    {
        return await (
            from a in db.CustomerPortalAccesses.AsNoTracking()
            join c in db.Contacts.AsNoTracking() on a.ContactId equals c.Id
            join cust in db.Customers.AsNoTracking() on a.CustomerId equals cust.Id
            orderby a.LastLoginAt descending
            select new PortalAccessRowModel(
                a.Id, a.ContactId, a.CustomerId, cust.Name,
                c.FirstName, c.LastName, c.Email,
                a.IsEnabled, a.LastLoginAt, a.CreatedAt)
        ).ToListAsync(ct);
    }
}

/// <summary>
/// Phase 1r — provision a new portal-access row for an existing Contact.
/// Idempotent: if the contact already has a row, return it (enabled or
/// disabled — admin can flip via the toggle endpoint). Requires the
/// contact to have an Email, since email is the portal login identifier.
/// </summary>
public record CreatePortalAccessCommand(int ContactId) : IRequest<PortalAccessRowModel>;

public class CreatePortalAccessHandler(AppDbContext db) : IRequestHandler<CreatePortalAccessCommand, PortalAccessRowModel>
{
    public async Task<PortalAccessRowModel> Handle(CreatePortalAccessCommand request, CancellationToken ct)
    {
        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId, ct)
            ?? throw new KeyNotFoundException($"Contact {request.ContactId} not found.");

        if (string.IsNullOrWhiteSpace(contact.Email))
            throw new InvalidOperationException(
                "Contact has no email address. Portal access uses email as the login identifier.");

        var existing = await db.CustomerPortalAccesses
            .FirstOrDefaultAsync(a => a.ContactId == request.ContactId, ct);
        if (existing is not null)
        {
            // Idempotent: surface the existing row instead of failing. Admin
            // flips IsEnabled via the toggle endpoint if needed.
            var customerNameExisting = await db.Customers.AsNoTracking()
                .Where(c => c.Id == existing.CustomerId)
                .Select(c => c.Name).FirstAsync(ct);
            return new PortalAccessRowModel(
                existing.Id, existing.ContactId, existing.CustomerId, customerNameExisting,
                contact.FirstName, contact.LastName, contact.Email,
                existing.IsEnabled, existing.LastLoginAt, existing.CreatedAt);
        }

        var access = new CustomerPortalAccess
        {
            ContactId = contact.Id,
            CustomerId = contact.CustomerId,
            IsEnabled = true,
        };
        db.CustomerPortalAccesses.Add(access);

        db.LogActivityAt(
            "portal-access-provisioned",
            $"Portal access provisioned for {contact.FirstName} {contact.LastName}.",
            ("Contact", contact.Id), ("Customer", contact.CustomerId));

        await db.SaveChangesAsync(ct);

        var customerName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == contact.CustomerId)
            .Select(c => c.Name).FirstAsync(ct);
        return new PortalAccessRowModel(
            access.Id, access.ContactId, access.CustomerId, customerName,
            contact.FirstName, contact.LastName, contact.Email,
            access.IsEnabled, access.LastLoginAt, access.CreatedAt);
    }
}

public record SetPortalAccessEnabledCommand(int AccessId, bool Enabled) : IRequest;

public class SetPortalAccessEnabledHandler(AppDbContext db) : IRequestHandler<SetPortalAccessEnabledCommand>
{
    public async Task Handle(SetPortalAccessEnabledCommand request, CancellationToken ct)
    {
        var access = await db.CustomerPortalAccesses.FirstOrDefaultAsync(a => a.Id == request.AccessId, ct)
            ?? throw new KeyNotFoundException($"Portal access {request.AccessId} not found.");
        if (access.IsEnabled == request.Enabled) return;
        access.IsEnabled = request.Enabled;
        // If revoking, clear any pending magic-link token so the contact can't bypass.
        if (!request.Enabled)
        {
            access.OneTimeTokenHash = null;
            access.OneTimeTokenExpiresAt = null;
        }
        db.LogActivityAt(
            request.Enabled ? "portal-access-enabled" : "portal-access-revoked",
            request.Enabled ? "Portal access enabled by admin" : "Portal access revoked by admin",
            ("Contact", access.ContactId), ("Customer", access.CustomerId));
        await db.SaveChangesAsync(ct);
    }
}
