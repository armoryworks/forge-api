using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Accounts;

// ─── Account list / detail / create / update ───────────────────────────

public record GetAccountsQuery() : IRequest<List<AccountResponseModel>>;

public class GetAccountsHandler(AppDbContext db) : IRequestHandler<GetAccountsQuery, List<AccountResponseModel>>
{
    public async Task<List<AccountResponseModel>> Handle(GetAccountsQuery request, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var contactCounts = await db.AccountContacts.AsNoTracking()
            .Where(c => ids.Contains(c.AccountId))
            .GroupBy(c => c.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, ct);
        var leadCounts = await db.Leads.AsNoTracking()
            .Where(l => l.AccountId != null && ids.Contains(l.AccountId.Value))
            .GroupBy(l => l.AccountId!.Value)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, ct);

        return accounts.Select(a => Map(a, contactCounts.GetValueOrDefault(a.Id, 0), leadCounts.GetValueOrDefault(a.Id, 0))).ToList();
    }

    private static AccountResponseModel Map(Account a, int contactCount, int leadCount) =>
        new(a.Id, a.Name, a.Description, a.Industry, a.Website, a.Phone, a.Address, a.City, a.State,
            a.PostalCode, a.Country, a.SizeBracket, a.OwnerUserId, contactCount, leadCount, a.CreatedAt);
}

public record GetAccountByIdQuery(int Id) : IRequest<AccountResponseModel>;

public class GetAccountByIdHandler(AppDbContext db) : IRequestHandler<GetAccountByIdQuery, AccountResponseModel>
{
    public async Task<AccountResponseModel> Handle(GetAccountByIdQuery request, CancellationToken ct)
    {
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Account {request.Id} not found.");
        var contactCount = await db.AccountContacts.AsNoTracking().CountAsync(c => c.AccountId == a.Id, ct);
        var leadCount = await db.Leads.AsNoTracking().CountAsync(l => l.AccountId == a.Id, ct);
        return new AccountResponseModel(a.Id, a.Name, a.Description, a.Industry, a.Website, a.Phone, a.Address,
            a.City, a.State, a.PostalCode, a.Country, a.SizeBracket, a.OwnerUserId, contactCount, leadCount, a.CreatedAt);
    }
}

public record CreateAccountCommand(CreateAccountRequest Request) : IRequest<AccountResponseModel>;

public class CreateAccountHandler(AppDbContext db, IHttpContextAccessor http) : IRequestHandler<CreateAccountCommand, AccountResponseModel>
{
    public async Task<AccountResponseModel> Handle(CreateAccountCommand request, CancellationToken ct)
    {
        var r = request.Request;
        if (string.IsNullOrWhiteSpace(r.Name)) throw new InvalidOperationException("Name is required.");
        var ownerId = int.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var a = new Account
        {
            Name = r.Name.Trim(),
            Description = r.Description?.Trim(),
            Industry = r.Industry?.Trim(),
            Website = r.Website?.Trim(),
            Phone = r.Phone?.Trim(),
            Address = r.Address?.Trim(),
            City = r.City?.Trim(),
            State = r.State?.Trim(),
            PostalCode = r.PostalCode?.Trim(),
            Country = r.Country?.Trim(),
            SizeBracket = r.SizeBracket?.Trim(),
            OwnerUserId = ownerId,
        };
        db.Accounts.Add(a);
        await db.SaveChangesAsync(ct);
        return new AccountResponseModel(a.Id, a.Name, a.Description, a.Industry, a.Website, a.Phone, a.Address,
            a.City, a.State, a.PostalCode, a.Country, a.SizeBracket, a.OwnerUserId, 0, 0, a.CreatedAt);
    }
}

public record UpdateAccountCommand(int Id, UpdateAccountRequest Request) : IRequest<AccountResponseModel>;

public class UpdateAccountHandler(AppDbContext db) : IRequestHandler<UpdateAccountCommand, AccountResponseModel>
{
    public async Task<AccountResponseModel> Handle(UpdateAccountCommand request, CancellationToken ct)
    {
        var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Account {request.Id} not found.");
        var r = request.Request;
        var changed = new List<string>();
        if (r.Name?.Trim() != a.Name && !string.IsNullOrWhiteSpace(r.Name)) { a.Name = r.Name.Trim(); changed.Add("name"); }
        if (r.Description?.Trim() != a.Description) { a.Description = r.Description?.Trim(); changed.Add("description"); }
        if (r.Industry?.Trim() != a.Industry) { a.Industry = r.Industry?.Trim(); changed.Add("industry"); }
        if (r.Website?.Trim() != a.Website) { a.Website = r.Website?.Trim(); changed.Add("website"); }
        if (r.Phone?.Trim() != a.Phone) { a.Phone = r.Phone?.Trim(); changed.Add("phone"); }
        if (r.Address?.Trim() != a.Address) { a.Address = r.Address?.Trim(); changed.Add("address"); }
        if (r.City?.Trim() != a.City) { a.City = r.City?.Trim(); changed.Add("city"); }
        if (r.State?.Trim() != a.State) { a.State = r.State?.Trim(); changed.Add("state"); }
        if (r.PostalCode?.Trim() != a.PostalCode) { a.PostalCode = r.PostalCode?.Trim(); changed.Add("postalCode"); }
        if (r.Country?.Trim() != a.Country) { a.Country = r.Country?.Trim(); changed.Add("country"); }
        if (r.SizeBracket?.Trim() != a.SizeBracket) { a.SizeBracket = r.SizeBracket?.Trim(); changed.Add("sizeBracket"); }
        if (r.OwnerUserId != a.OwnerUserId) { a.OwnerUserId = r.OwnerUserId; changed.Add("ownerUserId"); }
        if (changed.Count > 0) db.LogActivityAt("account-updated", $"Updated {changed.Count} fields: {string.Join(", ", changed)}", ("Account", a.Id));
        await db.SaveChangesAsync(ct);
        var contactCount = await db.AccountContacts.AsNoTracking().CountAsync(c => c.AccountId == a.Id, ct);
        var leadCount = await db.Leads.AsNoTracking().CountAsync(l => l.AccountId == a.Id, ct);
        return new AccountResponseModel(a.Id, a.Name, a.Description, a.Industry, a.Website, a.Phone, a.Address,
            a.City, a.State, a.PostalCode, a.Country, a.SizeBracket, a.OwnerUserId, contactCount, leadCount, a.CreatedAt);
    }
}

// ─── Account contacts ───────────────────────────────────────────────────

public record GetAccountContactsQuery(int AccountId) : IRequest<List<AccountContactResponseModel>>;

public class GetAccountContactsHandler(AppDbContext db) : IRequestHandler<GetAccountContactsQuery, List<AccountContactResponseModel>>
{
    public async Task<List<AccountContactResponseModel>> Handle(GetAccountContactsQuery request, CancellationToken ct)
    {
        return await db.AccountContacts.AsNoTracking()
            .Where(c => c.AccountId == request.AccountId)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.LastName)
            .Select(c => new AccountContactResponseModel(c.Id, c.AccountId, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary))
            .ToListAsync(ct);
    }
}

public record CreateAccountContactCommand(int AccountId, UpsertAccountContactRequest Request) : IRequest<AccountContactResponseModel>;

public class CreateAccountContactHandler(AppDbContext db) : IRequestHandler<CreateAccountContactCommand, AccountContactResponseModel>
{
    public async Task<AccountContactResponseModel> Handle(CreateAccountContactCommand request, CancellationToken ct)
    {
        var r = request.Request;
        // Enforce single primary — if this one is primary, unset the others.
        if (r.IsPrimary)
        {
            await db.AccountContacts.Where(c => c.AccountId == request.AccountId && c.IsPrimary)
                .ForEachAsync(c => c.IsPrimary = false, ct);
        }
        var c = new AccountContact
        {
            AccountId = request.AccountId,
            FirstName = r.FirstName.Trim(),
            LastName = r.LastName.Trim(),
            Email = r.Email?.Trim(),
            Phone = r.Phone?.Trim(),
            Role = r.Role?.Trim(),
            IsPrimary = r.IsPrimary,
        };
        db.AccountContacts.Add(c);
        await db.SaveChangesAsync(ct);
        return new AccountContactResponseModel(c.Id, c.AccountId, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary);
    }
}

public record UpdateAccountContactCommand(int AccountId, int ContactId, UpsertAccountContactRequest Request) : IRequest<AccountContactResponseModel>;

public class UpdateAccountContactHandler(AppDbContext db) : IRequestHandler<UpdateAccountContactCommand, AccountContactResponseModel>
{
    public async Task<AccountContactResponseModel> Handle(UpdateAccountContactCommand request, CancellationToken ct)
    {
        var c = await db.AccountContacts.FirstOrDefaultAsync(x => x.Id == request.ContactId && x.AccountId == request.AccountId, ct)
            ?? throw new KeyNotFoundException($"Account contact {request.ContactId} not found on account {request.AccountId}.");
        var r = request.Request;
        if (r.IsPrimary && !c.IsPrimary)
        {
            await db.AccountContacts.Where(x => x.AccountId == request.AccountId && x.IsPrimary && x.Id != c.Id)
                .ForEachAsync(x => x.IsPrimary = false, ct);
        }
        c.FirstName = r.FirstName.Trim();
        c.LastName = r.LastName.Trim();
        c.Email = r.Email?.Trim();
        c.Phone = r.Phone?.Trim();
        c.Role = r.Role?.Trim();
        c.IsPrimary = r.IsPrimary;
        await db.SaveChangesAsync(ct);
        return new AccountContactResponseModel(c.Id, c.AccountId, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary);
    }
}

public record DeleteAccountContactCommand(int AccountId, int ContactId) : IRequest;

public class DeleteAccountContactHandler(AppDbContext db) : IRequestHandler<DeleteAccountContactCommand>
{
    public async Task Handle(DeleteAccountContactCommand request, CancellationToken ct)
    {
        var c = await db.AccountContacts.FirstOrDefaultAsync(x => x.Id == request.ContactId && x.AccountId == request.AccountId, ct)
            ?? throw new KeyNotFoundException($"Account contact {request.ContactId} not found on account {request.AccountId}.");
        c.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Soft-delete an Account. Refuses when leads still reference it to keep
/// the back-link intact for audit history — same defensive pattern as
/// LeadSource. Admins can null-out the account on the referencing leads
/// (or wait for them to convert / be lost) and retry.
/// </summary>
public record DeleteAccountCommand(int Id) : IRequest;

public class DeleteAccountHandler(AppDbContext db) : IRequestHandler<DeleteAccountCommand>
{
    public async Task Handle(DeleteAccountCommand request, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Account {request.Id} not found.");

        var leadCount = await db.Leads.AsNoTracking().CountAsync(l => l.AccountId == account.Id, ct);
        if (leadCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete account — {leadCount} lead(s) reference it. Unassign the account on each lead first.");

        account.DeletedAt = DateTimeOffset.UtcNow;
        db.LogActivityAt("account-deleted", $"Deleted account '{account.Name}'.", ("Account", account.Id));
        await db.SaveChangesAsync(ct);
    }
}
