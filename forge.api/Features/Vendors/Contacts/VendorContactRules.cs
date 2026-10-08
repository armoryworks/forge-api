using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Vendors.Contacts;

public static class VendorContactRules
{
    public static async Task<List<VendorContact>> ClearOtherPrimariesAsync(
        AppDbContext db, int vendorId, int keepContactId, CancellationToken ct)
    {
        var others = await db.VendorContacts
            .Where(c => c.VendorId == vendorId && c.IsPrimary && c.Id != keepContactId)
            .ToListAsync(ct);
        foreach (var other in others)
            other.IsPrimary = false;
        return others;
    }

    public static async Task EnsureVendorExistsAsync(AppDbContext db, int vendorId, CancellationToken ct)
    {
        if (!await db.Vendors.AnyAsync(v => v.Id == vendorId, ct))
            throw new KeyNotFoundException($"Vendor {vendorId} not found");
    }

    public static string DisplayName(VendorContact contact) =>
        $"{contact.FirstName} {contact.LastName}{(string.IsNullOrEmpty(contact.Role) ? "" : $" ({contact.Role})")}";

    public static string DescribeReplaced(List<VendorContact> demoted) =>
        demoted.Count == 0 ? "" : $"; replaces {string.Join(", ", demoted.Select(c => $"{c.FirstName} {c.LastName}"))} as primary";

    public static VendorContactResponseModel ToResponseModel(VendorContact c) => new(
        c.Id, c.VendorId, c.FirstName, c.LastName, c.Email, c.Phone, c.Mobile, c.Fax,
        c.Role, c.IsPrimary, c.IsActive, c.Notes);
}
