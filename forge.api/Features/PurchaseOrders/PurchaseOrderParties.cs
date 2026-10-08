using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public static class PurchaseOrderParties
{
    public const string ContactField = "vendorContactId";
    public const string AddressField = "vendorAddressId";
    public const string ShipToField = "shipToLocationId";

    public static async Task<VendorContact?> ResolveContactAsync(
        AppDbContext db, int vendorId, int? contactId, List<ValidationFailure> failures, CancellationToken ct)
    {
        if (contactId is not int id)
        {
            return await db.VendorContacts
                .AsNoTracking()
                .Where(c => c.VendorId == vendorId && c.IsActive && c.IsPrimary)
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync(ct);
        }

        var contact = await db.VendorContacts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.VendorId == vendorId && c.IsActive, ct);
        if (contact is null)
            failures.Add(new ValidationFailure(ContactField,
                "That contact is not an active contact of this vendor. Choose another contact.") { AttemptedValue = id });
        return contact;
    }

    public static async Task<VendorAddress?> ResolveAddressAsync(
        AppDbContext db, int vendorId, int? addressId, List<ValidationFailure> failures, CancellationToken ct)
    {
        if (addressId is not int id)
        {
            return await db.VendorAddresses
                .AsNoTracking()
                .Where(a => a.VendorId == vendorId && a.IsActive
                    && (a.AddressType == VendorAddressType.OrderFrom || a.AddressType == VendorAddressType.RemitTo))
                .OrderByDescending(a => a.AddressType == VendorAddressType.OrderFrom)
                .ThenByDescending(a => a.IsDefault)
                .ThenBy(a => a.Id)
                .FirstOrDefaultAsync(ct);
        }

        var address = await db.VendorAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.VendorId == vendorId && a.IsActive, ct);
        if (address is null)
            failures.Add(new ValidationFailure(AddressField,
                "That address is not an active address of this vendor. Choose another address.") { AttemptedValue = id });
        return address;
    }

    public static async Task<CompanyLocation?> ResolveShipToAsync(
        AppDbContext db, int? locationId, List<ValidationFailure> failures, CancellationToken ct)
    {
        if (locationId is not int id)
        {
            return await db.CompanyLocations
                .AsNoTracking()
                .Where(l => l.IsActive)
                .OrderByDescending(l => l.IsDefault)
                .ThenBy(l => l.Id)
                .FirstOrDefaultAsync(ct);
        }

        var location = await db.CompanyLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.IsActive, ct);
        if (location is null)
            failures.Add(new ValidationFailure(ShipToField,
                "That ship-to location is not an active company location. Choose another location.") { AttemptedValue = id });
        return location;
    }

    public static string ContactName(VendorContact contact) =>
        $"{contact.FirstName} {contact.LastName}".Trim();

    public static string OneLine(string? line1, string? line2, string? city, string? state, string? postalCode, string? country)
    {
        var cityState = string.Join(", ", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var cityLine = string.Join(" ", new[] { cityState, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.Join(", ", new[] { line1, line2, cityLine, country }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));
    }

    public static string OneLine(VendorAddress address) =>
        OneLine(address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);

    public static string OneLine(CompanyLocation location) =>
        OneLine(location.Line1, location.Line2, location.City, location.State, location.PostalCode, location.Country);
}
