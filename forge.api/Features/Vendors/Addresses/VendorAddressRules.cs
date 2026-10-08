using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Vendors.Addresses;

public static partial class VendorAddressRules
{
    [GeneratedRegex("^[A-Za-z]{2}$")]
    private static partial Regex TwoLetterRe();

    public static bool TryParseType(string? value, out VendorAddressType type)
    {
        type = default;
        return !string.IsNullOrWhiteSpace(value)
            && !value.Any(char.IsDigit)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out type)
            && Enum.IsDefined(type);
    }

    public static bool IsValidState(string? country, string? state) =>
        !IsUnitedStates(country) || (state is not null && TwoLetterRe().IsMatch(state.Trim()));

    private static bool IsUnitedStates(string? country) =>
        country is not null
        && (country.Trim().Equals("US", StringComparison.OrdinalIgnoreCase)
            || country.Trim().Equals("USA", StringComparison.OrdinalIgnoreCase));

    public static async Task EnsureVendorExistsAsync(AppDbContext db, int vendorId, CancellationToken ct)
    {
        if (!await db.Vendors.AnyAsync(v => v.Id == vendorId, ct))
            throw new KeyNotFoundException($"Vendor {vendorId} not found");
    }

    public static string Describe(VendorAddressType type) => type switch
    {
        VendorAddressType.RemitTo => "Remit-to",
        VendorAddressType.OrderFrom => "Order-from",
        VendorAddressType.ShipFrom => "Ship-from",
        VendorAddressType.Billing => "Billing",
        _ => "Other",
    };

    public static async Task<List<VendorAddress>> ClearOtherDefaultsAsync(
        AppDbContext db, int vendorId, int keepAddressId, VendorAddressType type, CancellationToken ct)
    {
        var others = await db.VendorAddresses
            .Where(a => a.VendorId == vendorId && a.IsDefault && a.AddressType == type && a.Id != keepAddressId)
            .ToListAsync(ct);
        foreach (var other in others)
            other.IsDefault = false;
        return others;
    }

    public static string DescribeReplaced(List<VendorAddress> demoted) =>
        demoted.Count == 0 ? "" : $"; replaces {string.Join(", ", demoted.Select(a => a.Label))} as default";

    public static VendorAddressResponseModel ToResponseModel(VendorAddress a) => new(
        a.Id, a.VendorId, a.Label, a.AddressType.ToString(), a.Line1, a.Line2, a.City,
        a.State, a.PostalCode, a.Country, a.IsDefault, a.IsActive);
}
