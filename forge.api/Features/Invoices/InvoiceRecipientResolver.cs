using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// Picks who an invoice goes to: a contact with the Billing role (the primary one first), then the
/// customer's own email, then the primary contact, then any contact with an email.
/// </summary>
public static class InvoiceRecipientResolver
{
    public const string SourceBillingContact = "billing-contact";
    public const string SourceCustomer = "customer";
    public const string SourcePrimaryContact = "primary-contact";

    private const string BillingRoleCode = "billing";

    public static async Task<InvoiceEmailRecipientResponseModel> ResolveAsync(
        AppDbContext db, int customerId, CancellationToken ct)
    {
        var customerEmail = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.Email)
            .FirstOrDefaultAsync(ct);

        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.Email != null && c.Email != "")
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Id)
            .Select(c => new { c.FirstName, c.LastName, c.Email, c.Role, c.IsPrimary })
            .ToListAsync(ct);

        var billingLabels = await db.ReferenceData.AsNoTracking()
            .Where(r => r.GroupCode == "contact_role" && r.Code == BillingRoleCode)
            .Select(r => r.Label)
            .ToListAsync(ct);
        billingLabels.Add(BillingRoleCode);

        var billing = contacts.FirstOrDefault(c =>
            c.Role is { } role && billingLabels.Any(l => string.Equals(l.Trim(), role.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (billing is not null)
            return new(billing.Email!.Trim(), SourceBillingContact, FormatName(billing.LastName, billing.FirstName));

        if (!string.IsNullOrWhiteSpace(customerEmail))
            return new(customerEmail.Trim(), SourceCustomer, null);

        var fallback = contacts.FirstOrDefault();
        if (fallback is not null)
            return new(fallback.Email!.Trim(), SourcePrimaryContact, FormatName(fallback.LastName, fallback.FirstName));

        return new(null, null, null);
    }

    private static string FormatName(string last, string first)
        => string.IsNullOrWhiteSpace(first) ? last : $"{last}, {first}";
}
